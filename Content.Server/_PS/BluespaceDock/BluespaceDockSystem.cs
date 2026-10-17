using System.Linq;
using System.Numerics;
using System.IO;
using System.Text;
using Content.Server._NF.Bank;
using Content.Shared._NF.Bank;
using Content.Server._NF.ShuttleRecords;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.GameTicking;
using Content.Server.StationEvents.Components;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._NF.Station.Components;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Server.Preferences.Managers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._NF.BindToStation;
using Content.Shared._NF.CCVar;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._PS.BluespaceDock;
using Content.Shared._PS.ShipRegistry;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Chat;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Forensics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Content.Shared.UserInterface;
using Content.Shared.Warps;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Server._PS.BluespaceDock;

/// <summary>
/// Palmtree: Bluespace Dock. Stores a player's docked civilian ship as a YAML file under
/// <c>UserData/bluespace_dock/</c> and lets them spawn it back out (or sell it) later.
/// </summary>
/// <remarks>
/// The save/load mechanics are modelled on coyote-frontier's (unfinished) bluespace drydock:
/// power the ship down, delete its station (stations are mapped entities, which
/// <see cref="MissingEntityBehaviour.IncludeNullspace"/> would otherwise choke on), undock, then
/// <see cref="MapLoaderSystem.TrySaveGrid"/> with IncludeNullspace and an Ignore fallback. Loading
/// goes through the engine's own map loader, which initializes entities itself.
///
/// Unlike the drydock this is synchronous (like the shipyard's own purchase path), keeps its data in
/// a persistent registry file instead of an ID card, and charges configurable taxes.
/// </remarks>
public sealed class BluespaceDockSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IResourceManager _resource = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly AccessSystem _accessSystem = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly ShuttleRecordsSystem _shuttleRecords = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly ShipyardSystem _shipyard = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedBluespaceDockSystem _dock = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private readonly GunSystem _gunSystem = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    /// <summary>Name of the registry file. It lives next to the ship files, so anything that walks
    /// that folder has to skip it.</summary>
    public const string RegistryFileName = "registry.yml";

    /// <summary>Registry file, relative to the server user data folder.</summary>
    public const string RegistryPath = ShipDirectory + "/" + RegistryFileName;

    /// <summary>Directory ship files live in, relative to the server user data folder.</summary>
    public const string ShipDirectory = "/bluespace_dock";


    /// <summary>Groups a ship may belong to to be storable.</summary>
    private static readonly ShipyardConsoleUiKey[] AllowedGroups =
    [
        ShipyardConsoleUiKey.Shipyard, // civilian
        ShipyardConsoleUiKey.Scrap,
        ShipyardConsoleUiKey.Expedition,
    ];

    private BluespaceDockRegistry? _registry;
    private ISawmill _sawmill = default!;



    /// <summary>Ships currently being stored or retrieved, to prevent overlapping operations.</summary>
    private readonly HashSet<EntityUid> _busyShips = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("bluespace_dock");

        SubscribeLocalEvent<BluespaceDockConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<BluespaceDockConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<BluespaceDockConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<BluespaceDockConsoleComponent, BluespaceDockStoreMessage>(OnStoreMessage);
        SubscribeLocalEvent<BluespaceDockConsoleComponent, BluespaceDockRetrieveMessage>(OnRetrieveMessage);
        SubscribeLocalEvent<BluespaceDockConsoleComponent, BluespaceDockSellStoredMessage>(OnSellStoredMessage);
        SubscribeLocalEvent<BluespaceDockConsoleComponent, BluespaceDockSellActiveMessage>(OnSellActiveMessage);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // The registry lives on disk; drop the cache so the next round re-reads it.
        _registry = null;
        _busyShips.Clear();
    }

    #region Registry

    /// <summary>
    /// The in-memory registry, loaded from disk on first use. Exposed for tests.
    /// </summary>
    public BluespaceDockRegistry Registry
    {
        get
        {
            if (_registry is null)
            {
                _registry = LoadRegistry();
                PurgeMissingEntries();
            }

            return _registry;
        }
    }

    /// <summary>
    /// Drops the cached registry so it is re-read from disk (used on round restart and by tests).
    /// </summary>
    public void ReloadRegistry()
    {
        _registry = null;
    }

    /// <summary>
    /// Self-healing: registry entries whose ship file is gone are worthless, so they are removed.
    /// </summary>
    public void PurgeMissingEntries()
    {
        if (_registry is null)
            return;

        var removed = _registry.Entries.RemoveAll(entry => !ShipFileExists(entry.File));
        if (removed > 0)
        {
            _sawmill.Warning($"Purged {removed} bluespace dock entr{(removed == 1 ? "y" : "ies")} with missing ship files");
            SaveRegistry();
        }
    }

    private BluespaceDockRegistry LoadRegistry()
    {
        var registry = new BluespaceDockRegistry();
        var path = new ResPath(RegistryPath);

        try
        {
            if (!_resource.UserData.Exists(path))
                return registry;

            using var reader = _resource.UserData.OpenText(path);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count == 0)
                return registry;

            var node = yaml.Documents[0].RootNode.ToDataNode();
            registry = _serialization.Read<BluespaceDockRegistry>(node, notNullableOverride: true) ?? registry;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to read the bluespace dock registry at {path}: {e}");
        }

        return registry;
    }

    /// <summary>
    /// Writes the registry back to disk.
    /// </summary>
    public void SaveRegistry()
    {
        var path = new ResPath(RegistryPath);

        try
        {
            _resource.UserData.CreateDir(path.Directory);
            using var writer = _resource.UserData.OpenWriteText(path);
            _serialization.WriteValue(Registry, alwaysWrite: true, notNullableOverride: true).Write(writer);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to write the bluespace dock registry at {path}: {e}");
        }
    }

    /// <summary>
    /// Adds an entry and persists the registry.
    /// </summary>
    public void AddEntry(BluespaceDockEntry entry)
    {
        Registry.Entries.Add(entry);
        SaveRegistry();
    }

    /// <summary>
    /// Adds an entry and persists the registry, replacing an existing entry for the same owner and
    /// ship name. The new entry keeps its own ship id and file, so the replaced ship's file is left
    /// behind as an orphan for the admin restore tool.
    /// </summary>
    public void AddOrUpdateEntry(BluespaceDockEntry entry)
    {
        var existing = FindEntry(entry.OwnerUserId, entry.OwnerName, entry.DisplayName);
        if (existing != null)
            Registry.Entries.Remove(existing);

        Registry.Entries.Add(entry);
        SaveRegistry();
    }

    /// <summary>
    /// The entry a character has for a ship name, if any.
    /// </summary>
    public BluespaceDockEntry? FindEntry(string ownerUserId, string ownerName, string displayName)
    {
        foreach (var entry in Registry.Entries)
        {
            if (string.Equals(entry.OwnerUserId, ownerUserId, StringComparison.Ordinal)
                && string.Equals(entry.OwnerName, ownerName, StringComparison.Ordinal)
                && string.Equals(entry.DisplayName, displayName, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// A ship file with no registry entry: a leftover from a retrieval (files are kept on purpose)
    /// or from a crash. The admin restore tool lists, previews, restores and deletes these.
    /// </summary>
    public readonly record struct OrphanedShipFile(string Name, DateTime? Modified);

    /// <summary>
    /// Lists ship files that no registry entry points at.
    /// </summary>
    public List<OrphanedShipFile> GetOrphanedFiles()
    {
        var result = new List<OrphanedShipFile>();
        var shipPath = new ResPath(ShipDirectory);

        if (!_resource.UserData.Exists(shipPath))
            return result;

        foreach (var name in _resource.UserData.DirectoryEntries(shipPath))
        {
            if (!name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name == RegistryFileName)
                continue;

            var path = new ResPath($"{ShipDirectory}/{name}");
            if (_resource.UserData.IsDir(path) || Registry.FindByFile(name) != null)
                continue;

            result.Add(new OrphanedShipFile(name, GetFileModified(name)));
        }

        return result;
    }

    /// <summary>
    /// When a ship file was last written, if the storage provider has a real directory (the
    /// integration tests use an in-memory one).
    /// </summary>
    public DateTime? GetFileModified(string relativePath)
    {
        var root = _resource.UserData.RootDir;
        if (root == null)
            return null;

        try
        {
            var full = Path.Combine(root, ShipDirectory.TrimStart('/'), relativePath);
            return File.Exists(full) ? File.GetLastWriteTimeUtc(full) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads an orphaned ship file onto a new, paused map so an admin can look at it. The caller
    /// owns the grid and has to delete it.
    /// </summary>
    public bool TryLoadOrphanedFile(string fileName, out EntityUid? grid, out MapId mapId)
    {
        grid = null;
        mapId = MapId.Nullspace;

        if (!TryLoadShipFile(fileName, out var loaded) || loaded is not { } loadedGrid)
            return false;

        grid = loadedGrid;
        mapId = Transform(loadedGrid).MapID;
        _map.SetPaused(mapId, true);
        return true;
    }

    /// <summary>
    /// Deletes an orphaned ship file. Files that still have a registry entry are refused.
    /// </summary>
    public bool TryDeleteOrphanedFile(string fileName)
    {
        if (fileName == RegistryFileName || Registry.FindByFile(fileName) != null)
            return false;

        DeleteShipFile(fileName);
        return true;
    }

    /// <summary>
    /// Deletes every orphaned ship file (the registry and its entries are untouched).
    /// </summary>
    /// <returns>How many files were deleted.</returns>
    public int DeleteAllOrphanedFiles()
    {
        var deleted = 0;
        foreach (var orphan in GetOrphanedFiles())
        {
            if (TryDeleteOrphanedFile(orphan.Name))
                deleted++;
        }

        if (deleted > 0)
            _sawmill.Info($"Deleted {deleted} orphaned bluespace dock ship file(s)");

        return deleted;
    }

    /// <summary>
    /// Restores an orphaned ship file into a character's dock: the file moves back into the ship
    /// directory under a fresh id and a registry entry is written for it, so the character can pull
    /// the ship out of any dock console. Returns a localization key in <paramref name="error"/> on
    /// failure.
    /// </summary>
    public bool TryRestoreOrphanedFile(string fileName, string ownerUserId, string ownerName,
        string baseName, string? suffix, out string? error)
    {
        error = null;

        // Only files the orphan list actually offers may be restored: this rejects the registry
        // file, registered files, directories and anything else in the folder.
        var isOffered = false;
        foreach (var orphan in GetOrphanedFiles())
        {
            if (orphan.Name != fileName)
                continue;

            isOffered = true;
            break;
        }

        if (!isOffered)
        {
            error = "restore-orphan-error-not-orphaned";
            return false;
        }

        baseName = baseName.Trim().Replace('\n', ' ').Replace('\r', ' ');
        suffix = string.IsNullOrWhiteSpace(suffix) ? null : suffix.Trim().Replace('\n', ' ').Replace('\r', ' ');

        if (baseName.Length > ShipRegistrarConsoleComponent.MaxNameLength)
            baseName = baseName[..ShipRegistrarConsoleComponent.MaxNameLength];

        if (suffix is { Length: > ShipRegistrarConsoleComponent.MaxSuffixLength })
            suffix = suffix[..ShipRegistrarConsoleComponent.MaxSuffixLength];

        if (baseName.Length == 0)
        {
            error = "restore-orphan-error-bad-name";
            return false;
        }

        var displayName = ShipNameHelper.BuildFullName(baseName, suffix);
        if (FindEntry(ownerUserId, ownerName, displayName) != null)
        {
            error = "restore-orphan-error-name-in-use";
            return false;
        }

        // Load it once: this validates the file and gives the ship's value, model and identity.
        if (!TryLoadShipFile(fileName, out var loaded) || loaded is not { } loadedGrid)
        {
            error = "restore-orphan-error-load-failed";
            return false;
        }

        var value = (int) _pricing.AppraiseGrid(loadedGrid, null);
        var known = TryGetVesselInfo(loadedGrid, out var info);
        var model = known && !string.IsNullOrEmpty(info.Model) ? info.Model : "Custom";
        var vessel = info.Vessel?.Id;
        var mapId = Transform(loadedGrid).MapID;
        QueueDel(loadedGrid);
        _map.DeleteMap(mapId);

        var shipId = Guid.NewGuid().ToString("N");
        var newFile = GetShipFilePath(shipId);
        if (!TryMoveFile(fileName, newFile, out error))
            return false;

        AddEntry(new BluespaceDockEntry
        {
            ShipId = shipId,
            File = newFile,
            OwnerUserId = ownerUserId,
            OwnerName = ownerName,
            Vessel = vessel,
            Model = model,
            Name = baseName,
            DisplayName = displayName,
            Suffix = suffix,
            Value = value,
            StoredAt = DateTime.UtcNow.ToString("O"),
        });

        return true;
    }

    /// <summary>
    /// Makes sure a ship has a named warp point, the way shipyard ship files carry one, so admins can
    /// `warp` to it and ghosts can find it in the warp menu. Player-built ships never had one (they
    /// did not come from a ship file), and a file's warp keeps the name it was saved with - setting
    /// <see cref="WarpPointComponent.UseStationName"/> lets Frontier's station rename sync keep it in
    /// step with the ship's name afterwards.
    /// </summary>
    public void EnsureShipWarpPoint(EntityUid grid, string displayName)
    {
        if (FindShipWarpPoint(grid) is { } warp)
        {
            if (warp.Comp.Location != displayName)
                warp.Comp.Location = displayName;

            // Ship files bake the name in with this off; turn it on so renames follow.
            if (!warp.Comp.UseStationName)
                warp.Comp.UseStationName = true;

            Dirty(warp);
            return;
        }

        // Ship files put the warp on a bare entity of its own, roughly on the bridge; a hand-built
        // ship has no such spot, so the middle of the grid does.
        var center = Vector2.Zero;
        if (TryComp<MapGridComponent>(grid, out var gridComp))
            center = gridComp.LocalAABB.Center;

        var warpUid = Spawn(null, new EntityCoordinates(grid, center));
        var warpComp = EnsureComp<WarpPointComponent>(warpUid);
        warpComp.Location = displayName;
        warpComp.UseStationName = true;
        Dirty(warpUid, warpComp);
    }

    /// <summary>
    /// The warp point on a ship, if it has one.
    /// </summary>
    public Entity<WarpPointComponent>? FindShipWarpPoint(EntityUid grid)
    {
        var query = EntityQueryEnumerator<WarpPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var warp, out var xform))
        {
            if (xform.GridUid == grid)
                return (uid, warp);
        }

        return null;
    }

    /// <summary>
    /// Moves a ship file within the dock's storage.
    /// </summary>
    public bool TryMoveFile(string fromRelative, string toRelative, out string? error)
    {
        error = null;

        try
        {
            var from = new ResPath($"{ShipDirectory}/{fromRelative}");
            var to = new ResPath($"{ShipDirectory}/{toRelative}");

            if (!_resource.UserData.Exists(from))
            {
                error = "restore-orphan-error-file-missing";
                return false;
            }

            _resource.UserData.Rename(from, to);
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to move bluespace dock ship file {fromRelative} to {toRelative}: {e}");
            error = "restore-orphan-error-move-failed";
            return false;
        }
    }

    /// <summary>
    /// Removes an entry and persists the registry. <paramref name="deleteFile"/> also deletes the
    /// ship file (used when a stored ship is sold).
    /// </summary>
    public void RemoveEntry(BluespaceDockEntry entry, bool deleteFile)
    {
        Registry.Entries.Remove(entry);
        SaveRegistry();

        if (deleteFile)
            DeleteShipFile(entry.File);
    }

    /// <summary>
    /// Deletes a ship file, ignoring failures.
    /// </summary>
    public void DeleteShipFile(string relativePath)
    {
        try
        {
            var path = new ResPath($"{ShipDirectory}/{relativePath}");
            if (_resource.UserData.Exists(path))
                _resource.UserData.Delete(path);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Failed to delete bluespace dock ship file {relativePath}: {e}");
        }
    }

    /// <summary>
    /// Path of a ship file relative to <see cref="ShipDirectory"/>.
    /// </summary>
    public static string GetShipFilePath(string shipId)
    {
        return $"{shipId}.yml";
    }

    #endregion Registry

    #region Ship files

    /// <summary>
    /// Serializes a grid into <c>UserData/bluespace_dock/&lt;relativePath&gt;</c> and verifies that
    /// the written file parses as YAML. Returns false with a reason on any failure.
    /// </summary>
    /// <remarks>
    /// The ship is prepared first: the deed and everything referencing its station (a mapped entity
    /// the file cannot carry) is stripped, so a written file is always loadable.
    /// <paramref name="stripped"/> receives what was removed so the caller can restore it when the
    /// save fails; a caller that does not pass a list leaves the ship stripped.
    /// </remarks>
    public bool TrySaveShipFile(EntityUid shuttleUid, string relativePath, out string? error,
        List<(EntityUid Ent, EntityUid Station, bool Member, bool Bound)>? stripped = null)
    {
        error = null;
        var path = new ResPath($"{ShipDirectory}/{relativePath}");

        var removed = StripStationReferences(shuttleUid);
        stripped?.AddRange(removed);

        try
        {
            _resource.UserData.CreateDir(path.Directory);

            var options = new SerializationOptions
            {
                MissingEntityBehaviour = MissingEntityBehaviour.IncludeNullspace,
                ErrorOnOrphan = false,
            };

            if (!_mapLoader.TrySaveGrid(shuttleUid, path, options))
            {
                _sawmill.Warning($"Failed to save grid {ToPrettyString(shuttleUid)} with IncludeNullspace, retrying with Ignore");

                options = new SerializationOptions
                {
                    MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
                    ErrorOnOrphan = false,
                };

                if (!_mapLoader.TrySaveGrid(shuttleUid, path, options))
                {
                    error = "bluespace-dock-error-save-failed";
                    return false;
                }
            }

            // Read the file back and parse it: a ship is only deleted once its file is known-good.
            string text;
            using (var reader = _resource.UserData.OpenText(path))
            {
                text = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "bluespace-dock-error-save-empty";
                return false;
            }

            var yaml = new YamlStream();
            using (var stringReader = new StringReader(text))
            {
                yaml.Load(stringReader);
            }

            if (yaml.Documents.Count == 0)
            {
                error = "bluespace-dock-error-save-empty";
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Exception while saving ship {ToPrettyString(shuttleUid)}: {e}");
            error = "bluespace-dock-error-save-failed";
            return false;
        }
    }

    /// <summary>
    /// The entity prototypes a ship file uses that no longer exist. The engine refuses a whole file over
    /// a single one of these, so they are collected before loading.
    /// </summary>
    public HashSet<string> CollectMissingPrototypes(ResPath path)
    {
        var missing = new HashSet<string>();

        try
        {
            using var reader = _resource.UserData.OpenText(path);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                return missing;

            // Map files group entities by prototype: a sequence of { proto, entities } mappings
            // ("type" in files older than format 4). YamlDotNet nodes compare by reference, so keys
            // are matched on their scalar value.
            YamlNode? entitiesNode = null;
            foreach (var (key, value) in root.Children)
            {
                if (key is YamlScalarNode { Value: "entities" })
                {
                    entitiesNode = value;
                    break;
                }
            }

            if (entitiesNode is not YamlSequenceNode entities)
                return missing;

            foreach (var node in entities.Children)
            {
                if (node is not YamlMappingNode group)
                    continue;

                foreach (var (key, value) in group.Children)
                {
                    if (key is not YamlScalarNode { Value: "proto" or "type" })
                        continue;

                    if (value is YamlScalarNode scalar
                        && !string.IsNullOrWhiteSpace(scalar.Value)
                        && !_prototype.HasIndex<EntityPrototype>(scalar.Value))
                    {
                        missing.Add(scalar.Value);
                    }

                    break;
                }
            }
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Failed to scan {path} for removed prototypes: {e}");
        }

        return missing;
    }

    /// <summary>
    /// Writes a copy of a ship file with the entities whose prototype no longer exists removed, so the
    /// loader can read it (the engine refuses a whole file over a single missing prototype). The
    /// original is left untouched for recovery. Returns false when there is nothing to fix.
    /// </summary>
    /// <remarks>
    /// The engine's own <c>BeforeEntityReadEvent.DeletedPrototypes</c> hook is not used here: it creates
    /// those entities without their prototype, which throws on components with required fields
    /// (`MeleeWeaponComponent.damage`, for instance) and takes the whole load down with it. Dropping
    /// the entities instead only leaves the references to them invalid, which the deserializer logs
    /// and carries on from.
    /// </remarks>
    public bool TryWriteSanitizedShipFile(ResPath path, HashSet<string> missing, out string? sanitizedRelative)
    {
        sanitizedRelative = null;

        if (missing.Count == 0)
            return false;

        try
        {
            MappingDataNode root;
            using (var reader = _resource.UserData.OpenText(path))
            {
                var yaml = new YamlStream();
                yaml.Load(reader);

                if (yaml.Documents.Count == 0
                    || yaml.Documents[0].RootNode.ToDataNode() is not MappingDataNode mapping)
                {
                    return false;
                }

                root = mapping;
            }

            var entities = root.Get<SequenceDataNode>("entities");

            // Entities whose prototype is gone, plus everything parented to them (their contents and
            // so on), so nothing is left referring to a uid that is not in the file any more.
            var dropped = new HashSet<string>();
            foreach (var node in entities)
            {
                if (node is MappingDataNode group
                    && group.TryGet<ValueDataNode>("proto", out var proto)
                    && missing.Contains(proto.Value))
                {
                    CollectEntityUids(group, dropped);
                }
            }

            if (dropped.Count == 0)
                return false;

            bool grew;
            do
            {
                grew = false;
                foreach (var node in entities)
                {
                    if (node is not MappingDataNode group)
                        continue;

                    foreach (var (uid, parent) in GetEntityParents(group))
                    {
                        if (parent != null && dropped.Contains(parent) && dropped.Add(uid))
                            grew = true;
                    }
                }
            } while (grew);

            var kept = new SequenceDataNode();
            foreach (var node in entities)
            {
                if (node is not MappingDataNode group)
                {
                    kept.Add(node);
                    continue;
                }

                var keptEntities = new SequenceDataNode();
                foreach (var entityNode in group.Get<SequenceDataNode>("entities"))
                {
                    if (entityNode is MappingDataNode entity
                        && entity.TryGet<ValueDataNode>("uid", out var uid)
                        && dropped.Contains(uid.Value))
                    {
                        continue;
                    }

                    keptEntities.Add(entityNode);
                }

                if (keptEntities.Count == 0)
                    continue;

                group["entities"] = keptEntities;
                kept.Add(group);
            }

            root["entities"] = kept;

            var name = $"migrated-{Guid.NewGuid():N}.yml";
            var destination = new ResPath($"{ShipDirectory}/{name}");
            _resource.UserData.CreateDir(new ResPath(ShipDirectory));

            using (var writer = _resource.UserData.OpenWriteText(destination))
            {
                root.Write(writer);
            }

            sanitizedRelative = name;
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to write a sanitized copy of {path}: {e}");
            return false;
        }
    }

    /// <summary>
    /// Every entity uid in a prototype group.
    /// </summary>
    private static void CollectEntityUids(MappingDataNode group, HashSet<string> uids)
    {
        if (!group.TryGet<SequenceDataNode>("entities", out var entities))
            return;

        foreach (var node in entities)
        {
            if (node is MappingDataNode entity && entity.TryGet<ValueDataNode>("uid", out var uid))
                uids.Add(uid.Value);
        }
    }

    /// <summary>
    /// Every entity uid in a prototype group with the uid its Transform is parented to, when the file
    /// says so (an implicit parent from the prototype is not in the file).
    /// </summary>
    private static IEnumerable<(string Uid, string? Parent)> GetEntityParents(MappingDataNode group)
    {
        if (!group.TryGet<SequenceDataNode>("entities", out var entities))
            yield break;

        foreach (var node in entities)
        {
            if (node is not MappingDataNode entity || !entity.TryGet<ValueDataNode>("uid", out var uid))
                continue;

            string? parent = null;
            if (entity.TryGet<SequenceDataNode>("components", out var components))
            {
                foreach (var componentNode in components)
                {
                    if (componentNode is not MappingDataNode component
                        || !component.TryGet<ValueDataNode>("type", out var type)
                        || type.Value != "Transform")
                    {
                        continue;
                    }

                    if (component.TryGet<ValueDataNode>("parent", out var parentNode))
                        parent = parentNode.Value;

                    break;
                }
            }

            yield return (uid.Value, parent);
        }
    }

    /// <summary>
    /// Loads a ship file from <c>UserData/bluespace_dock/</c> onto a fresh map and restores the
    /// map-init registration the loader skips for live-saved grids (device network membership and
    /// shuttle cannon signal ports; see <see cref="RestoreMapInitRegistration"/>).
    /// </summary>
    public bool TryLoadShipFile(string relativePath, out EntityUid? shuttleUid)
    {
        shuttleUid = null;
        var path = new ResPath($"{ShipDirectory}/{relativePath}");

        string? sanitizedRelative = null;

        try
        {
            if (!_resource.UserData.Exists(path))
                return false;

            // An update may have removed something the ship uses: the engine refuses a whole file over
            // one missing prototype, so those entities are dropped from a copy and the copy is loaded.
            var missing = CollectMissingPrototypes(path);
            if (missing.Count > 0)
            {
                _sawmill.Warning($"Ship file {relativePath} uses {missing.Count} prototype(s) that no longer exist " +
                                 $"({string.Join(", ", missing)}); those entities are dropped for this load");

                if (TryWriteSanitizedShipFile(path, missing, out var copy))
                {
                    sanitizedRelative = copy;
                    path = new ResPath($"{ShipDirectory}/{copy}");
                }
            }

            _map.CreateMap(out var mapId);
            if (!_mapLoader.TryLoadGrid(mapId, path, out var grid))
            {
                _map.DeleteMap(mapId);
                return false;
            }

            shuttleUid = grid.Value.Owner;

            var entities = new HashSet<EntityUid>();
            GetEntitiesOnGrid(shuttleUid.Value, entities);

            // Files written before the save cleaned up after itself (or hand-edited ones) can still
            // carry a deed and station references; a station with an invalid uid makes
            // GetOwningStation return EntityUid.Invalid and MapInit handlers such as the holopad
            // rename throw. Strip them before anything initializes.
            foreach (var ent in entities)
            {
                RemComp<ShuttleDeedComponent>(ent);
                RemComp<StationMemberComponent>(ent);
                RemComp<StationBoundObjectComponent>(ent);

                // Docking is runtime state: a file that still says the ship is docked (to a dock
                // that no longer exists, or to its old station) makes the next dock attempt reuse a
                // joint id that is already taken.
                if (TryComp<DockingComponent>(ent, out var dock))
                {
                    dock.DockedWith = null;
                    dock.DockJoint = null;
                    dock.DockJointId = null;
                }
            }

            // A stored ship is saved from a live grid, so every entity in the file carries
            // `mapInit: true` and the engine deliberately **skips MapInitEvent** when loading them
            // (it only bumps their life stage - that is what makes a re-loaded live save keep its
            // container contents instead of re-running spawn-time fills). Registration work that
            // normally happens in MapInit is therefore missing: devices are not on the device
            // network (buttons and logic silently do nothing) and shuttle cannons never get their
            // signal ports. Restore just that registration here - do NOT re-raise MapInit for the
            // entities, or every storage/container fill re-runs and duplicates or spills items.
            RestoreMapInitRegistration(shuttleUid.Value);

            // No MapInit re-raise here: the engine already initializes loaded entities itself, in
            // hierarchy order (EntityDeserializer.StartEntitiesInternal walks parents first), which is
            // what every map load and the shipyard's own purchase rely on. Re-raising MapInit on top
            // of that double-initializes components - storage fills its prototype contents a second
            // time (duplicating tools, or failing with "No room!"), item slots spawn their starting
            // items again, and so on.
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Exception while loading ship file {relativePath}: {e}");

            // Never leave a half-loaded ship behind on a fresh map.
            if (shuttleUid is { } loaded && Exists(loaded))
            {
                var mapId = Transform(loaded).MapID;
                QueueDel(loaded);
                _map.DeleteMap(mapId);
            }

            shuttleUid = null;
            return false;
        }
        finally
        {
            if (sanitizedRelative != null)
                DeleteShipFile(sanitizedRelative);
        }
    }

    #endregion Ship files

    #region Vessel identity

    /// <summary>
    /// Resolves a grid's vessel identity: a shipyard vessel prototype from the ship's station, or
    /// the Ship Registrar's marker for hand-built ships. Returns false when the grid has neither.
    /// </summary>
    public bool TryGetVesselInfo(EntityUid shuttleUid, out BluespaceDockVesselInfo info)
    {
        info = default;

        if (_station.GetOwningStation(shuttleUid) is { Valid: true } station
            && TryComp<ExtraShuttleInformationComponent>(station, out var extra)
            && extra.Vessel is { } vesselId
            && _prototype.TryIndex(vesselId, out VesselPrototype? vessel))
        {
            info = new BluespaceDockVesselInfo(vesselId, vessel.Group.ToString(), vessel.Name);
            return true;
        }

        if (TryComp<RegisteredVesselComponent>(shuttleUid, out var registered))
        {
            info = new BluespaceDockVesselInfo(null, registered.Group.ToString(), registered.Model);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the vessel's group may be stored in the dock (civilian, scrap or expedition).
    /// </summary>
    public static bool IsAllowedGroup(string group)
    {
        foreach (var allowed in AllowedGroups)
        {
            if (string.Equals(allowed.ToString(), group, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    #endregion Vessel identity

    #region Checks

    /// <summary>
    /// Whether the shuttle is docked to the given grid (the shipyard's sale check).
    /// </summary>
    private bool IsDockedToGrid(EntityUid shuttleUid, EntityUid gridUid)
    {
        var gridDocks = _docking.GetDocks(gridUid);
        var shuttleDocks = _docking.GetDocks(shuttleUid);

        foreach (var shuttleDock in shuttleDocks)
        {
            foreach (var gridDock in gridDocks)
            {
                if (shuttleDock.Comp.DockedWith == gridDock.Owner)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Looks for a living, sapient being aboard (the shipyard's own check).
    /// </summary>
    private string? FoundOrganics(EntityUid uid)
    {
        return _shipyard.FoundOrganics(uid, GetEntityQuery<MobStateComponent>(), GetEntityQuery<TransformComponent>());
    }

    /// <summary>
    /// Whether another live ship is already flying with this name. Grids without a station (left
    /// behind by an old, failed retrieval, for instance) do not count: they are not real vessels and
    /// would otherwise block the name forever.
    /// </summary>
    private bool ShipNameInUse(string displayName)
    {
        var query = EntityQueryEnumerator<ShuttleDeedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var deed, out var xform))
        {
            if (xform.GridUid != uid)
                continue;

            if (_station.GetOwningStation(uid) is not { Valid: true })
                continue;

            if (string.Equals(ShipyardSystem.GetFullName(deed), displayName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Character identity used by the registry: the player's ckey plus their character name.
    /// </summary>
    private bool TryGetCharacter(EntityUid actor, EntityUid? targetId, out string userId, out string characterName)
    {
        userId = string.Empty;
        characterName = string.Empty;

        if (!_player.TryGetSessionByEntity(actor, out var session))
            return false;

        userId = session.UserId.ToString();

        string? idName = null;
        if (targetId is { Valid: true } id && TryComp<IdCardComponent>(id, out var idCard))
            idName = idCard.FullName;

        characterName = GetCharacterName(session.UserId, idName, session.AttachedEntity);
        return characterName.Length > 0;
    }

    /// <summary>
    /// The name a character is known by. The profile comes first because that is what per-character
    /// data is keyed on elsewhere (the bank, for instance), and it means a lost, replaced or renamed
    /// ID card does not lock a character out of their own ships. The inserted ID is the fallback
    /// (it is what an unmodified ID shows) and the mob's name is the last resort.
    /// </summary>
    public string GetCharacterName(NetUserId userId, string? idName, EntityUid? mob)
    {
        string? profileName = null;
        if (_prefs.TryGetCachedPreferences(userId, out var prefs)
            && prefs.SelectedCharacter is HumanoidCharacterProfile profile)
        {
            profileName = profile.Name;
        }

        string? mobName = null;
        if (mob is { } mobUid && Exists(mobUid))
            mobName = MetaData(mobUid).EntityName;

        return PickCharacterName(profileName, idName, mobName);
    }

    /// <summary>
    /// Picks a character name from the sources the dock has, in order of trust.
    /// </summary>
    public static string PickCharacterName(string? profileName, string? idName, string? mobName)
    {
        if (!string.IsNullOrWhiteSpace(profileName))
            return profileName!;

        if (!string.IsNullOrWhiteSpace(idName))
            return idName!;

        return string.IsNullOrWhiteSpace(mobName) ? string.Empty : mobName!;
    }

    /// <summary>
    /// Whether a registry entry belongs to a character. Entries written by the admin restore tool can
    /// have no ckey (an ID card does not carry one), so those match by character name alone.
    /// </summary>
    public static bool EntryBelongsToCharacter(BluespaceDockEntry entry, string userId, string characterName)
    {
        if (!string.Equals(entry.OwnerName, characterName, StringComparison.Ordinal))
            return false;

        return string.IsNullOrEmpty(entry.OwnerUserId)
               || string.Equals(entry.OwnerUserId, userId, StringComparison.Ordinal);
    }

    #endregion Checks

    #region UI

    private void OnConsoleOpened(EntityUid uid, BluespaceDockConsoleComponent component, BoundUIOpenedEvent args)
    {
        RefreshState(uid, args.Actor);
    }

    private void OnItemSlotChanged(EntityUid uid, BluespaceDockConsoleComponent component, ContainerModifiedMessage args)
    {
        if (args.Container.ID != component.TargetIdSlot.ID)
            return;

        if (!TryComp<ActivatableUIComponent>(uid, out var uiComp) || uiComp.Key is null)
            return;

        foreach (var user in _ui.GetActors(uid, uiComp.Key))
        {
            if (user is { Valid: true })
                RefreshState(uid, user);
        }
    }

    /// <summary>
    /// Rebuilds and pushes the console state.
    /// </summary>
    public void RefreshState(EntityUid consoleUid, EntityUid actor)
    {
        var component = Comp<BluespaceDockConsoleComponent>(consoleUid);
        var targetId = component.TargetIdSlot.ContainerSlot?.ContainedEntity;

        var hasId = targetId is { Valid: true };
        string? idName = null;
        var balance = 0;
        var hasActiveShip = false;
        string? activeShipName = null;
        var activeShipValue = 0;
        var storeCost = 0;
        var canStore = false;
        string? storeBlocked = null;
        var canSellActive = false;
        var stored = new List<BluespaceDockEntryUi>();

        if (hasId && TryComp<IdCardComponent>(targetId!.Value, out var card))
            idName = card.FullName;

        _bank.TryGetBalance(actor, out balance);

        if (hasId && TryComp<ShuttleDeedComponent>(targetId!.Value, out var deed) && deed.ShuttleUid is { Valid: true } shuttleUid)
        {
            hasActiveShip = true;
            activeShipName = ShipyardSystem.GetFullName(deed);
            activeShipValue = AppraiseShip(shuttleUid);
            storeCost = (int) (activeShipValue * _cfg.GetCVar(NFCCVars.BluespaceDockStoreTax));

            canSellActive = true;

            // Run the same rule ladder the store action runs, so the button state cannot drift from
            // what the server will actually accept.
            var stationGrid = _station.GetOwningStation(consoleUid) is { Valid: true } owningStation
                ? _station.GetLargestGrid(owningStation)
                : null;

            storeBlocked = FindStoreBlocker(shuttleUid, requireDocked: true, stationGrid);

            if (storeBlocked is null && balance < storeCost)
                storeBlocked = "bluespace-dock-error-insufficient-funds";

            if (storeBlocked is null)
            {
                var maxShips = _cfg.GetCVar(NFCCVars.BluespaceDockMaxShips);
                if (maxShips > 0 && TryGetCharacter(actor, targetId, out var userId, out var characterName)
                    && CountStoredShips(userId, characterName) >= maxShips)
                {
                    storeBlocked = "bluespace-dock-error-too-many-ships";
                }
            }

            canStore = storeBlocked is null;
        }

        if (hasId && TryGetCharacter(actor, targetId, out var userId2, out var characterName2))
        {
            var retrieveTax = _cfg.GetCVar(NFCCVars.BluespaceDockRetrieveTax);
            var sellRate = _cfg.GetCVar(NFCCVars.BluespaceDockStoredSellRate);

            foreach (var entry in Registry.Entries)
            {
                if (!EntryBelongsToCharacter(entry, userId2, characterName2))
                    continue;

                var fileExists = ShipFileExists(entry.File);
                var retrieveCost = (int) (entry.Value * retrieveTax);
                var canRetrieve = fileExists && balance >= retrieveCost && !ShipNameInUse(entry.DisplayName);

                string? blocked = null;
                if (!fileExists)
                    blocked = "bluespace-dock-error-file-missing";
                else if (balance < retrieveCost)
                    blocked = "bluespace-dock-error-insufficient-funds";
                else if (ShipNameInUse(entry.DisplayName))
                    blocked = "bluespace-dock-error-name-in-use";

                stored.Add(new BluespaceDockEntryUi(
                    entry.ShipId,
                    entry.DisplayName,
                    entry.Model,
                    entry.Value,
                    (int) (entry.Value * sellRate),
                    canRetrieve,
                    fileExists,
                    blocked));
            }
        }

        _ui.SetUiState(consoleUid, BluespaceDockUiKey.Key, new BluespaceDockUiState(
            hasId,
            idName,
            balance,
            hasActiveShip,
            activeShipName,
            activeShipValue,
            storeCost,
            canStore,
            storeBlocked,
            canSellActive,
            stored));
    }

    private int CountStoredShips(string userId, string characterName)
    {
        var count = 0;
        foreach (var entry in Registry.Entries)
        {
            if (EntryBelongsToCharacter(entry, userId, characterName))
                count++;
        }

        return count;
    }

    private bool ShipFileExists(string relativePath)
    {
        try
        {
            return _resource.UserData.Exists(new ResPath($"{ShipDirectory}/{relativePath}"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a ship file still parses as YAML. A file that does not is corrupt: its registry entry
    /// is dropped on the next use (a file that merely fails to load is kept, since that can be a
    /// transient content error rather than a broken save).
    /// </summary>
    public bool IsShipFileParseable(string relativePath)
    {
        try
        {
            var path = new ResPath($"{ShipDirectory}/{relativePath}");
            if (!_resource.UserData.Exists(path))
                return false;

            using var reader = _resource.UserData.OpenText(path);
            var yaml = new YamlStream();
            yaml.Load(reader);
            return yaml.Documents.Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    #endregion UI

    #region Ship state helpers

    /// <summary>
    /// Removes everything that ties a ship to its (mapped, not serializable) station, plus the deed:
    /// the station is deleted once the save succeeds, and stale station references make the
    /// deserializer log errors and MapInit handlers (holopads, for instance) throw on load. The deed
    /// is rebuilt from the dock registry when the ship comes back.
    /// </summary>
    /// <returns>What was removed, so a failed save can put it back.</returns>
    private List<(EntityUid Ent, EntityUid Station, bool Member, bool Bound)> StripStationReferences(EntityUid gridUid)
    {
        var stripped = new List<(EntityUid, EntityUid, bool, bool)>();
        var entities = new HashSet<EntityUid>();
        GetEntitiesOnGrid(gridUid, entities);

        foreach (var ent in entities)
        {
            RemComp<ShuttleDeedComponent>(ent);

            var hasMember = TryComp<StationMemberComponent>(ent, out var member);
            var hasBound = TryComp<StationBoundObjectComponent>(ent, out var bound);
            if (!hasMember && !hasBound)
                continue;

            var station = hasMember ? member!.Station : bound!.BoundStation ?? EntityUid.Invalid;
            stripped.Add((ent, station, hasMember, hasBound));

            if (hasMember)
                RemComp<StationMemberComponent>(ent);
            if (hasBound)
                RemComp<StationBoundObjectComponent>(ent);
        }

        return stripped;
    }

    /// <summary>
    /// Best-effort undo of <see cref="StripStationReferences"/> after a failed save. The station is
    /// still alive (it is only deleted once the file is known-good).
    /// </summary>
    private void RestoreStationReferences(EntityUid gridUid,
        List<(EntityUid Ent, EntityUid Station, bool Member, bool Bound)> stripped)
    {
        foreach (var (ent, station, member, bound) in stripped)
        {
            if (!Exists(ent) || !station.IsValid() || !Exists(station))
                continue;

            if (member)
            {
                var memberComp = EnsureComp<StationMemberComponent>(ent);
                memberComp.Station = station;
                Dirty(ent, memberComp);
            }

            if (bound)
            {
                var boundComp = EnsureComp<StationBoundObjectComponent>(ent);
                boundComp.BoundStation = station;
                Dirty(ent, boundComp);
            }
        }
    }

    /// <summary>
    /// Docks a ship back to its station after a failed store, so the player is not left undocked.
    /// </summary>
    private void RestoreDocking(EntityUid shuttleUid, EntityUid? station)
    {
        if (station is not { Valid: true } stationUid || !Exists(stationUid))
            return;

        if (!TryComp<ShuttleComponent>(shuttleUid, out var shuttleComp))
            return;

        if (_station.GetLargestGrid(stationUid) is { } stationGrid)
            _shuttle.TryFTLDock(shuttleUid, shuttleComp, stationGrid);
    }

    /// <summary>
    /// Restores the map-init registration the loader skips for a stored ship's entities (see the
    /// call in <see cref="TryLoadShipFile"/>): device network membership for every auto-connecting
    /// device, the signal ports of shuttle cannons, and the modified gun values (fire rate,
    /// projectile speed, angles) that MapInit computes for every <see cref="GunComponent"/>.
    /// </summary>
    public void RestoreMapInitRegistration(EntityUid grid)
    {
        var entities = new HashSet<EntityUid>();
        GetEntitiesOnGrid(grid, entities);

        foreach (var uid in entities)
        {
            if (!Exists(uid))
                continue;

            // DeviceNetworkSystem.OnMapInit: join the device network, unless the device was
            // deliberately disconnected or is already on it.
            if (TryComp<DeviceNetworkComponent>(uid, out var device)
                && device.AutoConnect
                && !_deviceNetwork.IsDeviceConnected(uid, device))
            {
                _deviceNetwork.ConnectDevice(uid, device);
            }

            // GunSignalControlSystem.OnInit: shuttle cannons get their signal sink ports here.
            if (TryComp<GunSignalControlComponent>(uid, out var gunControl))
                _deviceLink.EnsureSinkPorts(uid, gunControl.TriggerPort, gunControl.TogglePort, gunControl.OnPort, gunControl.OffPort);

            // SharedGunSystem.OnMapInit: refresh the modified gun values (fire rate, projectile
            // speed, angles, ...). They are computed in MapInit and are otherwise left at zero on a
            // loaded gun, so saved guns cannot fire and emitter bolts spawn with no velocity.
            if (TryComp<GunComponent>(uid, out var gun))
                _gunSystem.RefreshModifiers((uid, gun));
        }
    }

    /// <summary>
    /// Moves every ghost and mob off a grid before it is stored or deleted, so passengers (pets
    /// included), corpses and onlookers are never swallowed by the grid's deletion or saved into
    /// the ship file. Mobs inside containers (a pet in a crate, for instance) are taken out of
    /// them first. Returns how many entities were moved.
    /// </summary>
    public int EvacuateGrid(EntityUid grid)
    {
        var entities = new HashSet<EntityUid>();
        GetEntitiesOnGrid(grid, entities);

        var destination = GetEvacuationPoint(grid);
        var moved = 0;

        foreach (var uid in entities)
        {
            if (!Exists(uid))
                continue;

            if (!HasComp<GhostComponent>(uid) && !HasComp<MobStateComponent>(uid))
                continue;

            // A contained mob must be pulled out first, or it would stay inside a container that is
            // deleted (or saved) with the ship.
            if (_containers.TryGetContainingContainer((uid, null, null), out var container))
            {
                if (!_containers.Remove((uid, null, null), container, force: true))
                {
                    _sawmill.Warning($"Bluespace dock: could not take {ToPrettyString(uid)} out of its container while evacuating a grid");
                    continue;
                }
            }

            _transform.SetMapCoordinates(uid, destination);
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// Where a grid's occupants are moved before it is deleted: on the grid the ship is docked to
    /// (by the airlock it is attached to), or the default map (the station) when it is not docked
    /// anywhere.
    /// </summary>
    private MapCoordinates GetEvacuationPoint(EntityUid grid)
    {
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith is not { Valid: true } partner || !Exists(partner))
                continue;

            // Never "evacuate" onto the same grid (a self-dock would still be deleted).
            if (Transform(partner).GridUid == grid)
                continue;

            return _transform.GetMapCoordinates(partner);
        }

        return new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap);
    }

    #endregion Ship state helpers

    #region Store

    private void OnStoreMessage(EntityUid consoleUid, BluespaceDockConsoleComponent component, BluespaceDockStoreMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-id");
            return;
        }

        if (!TryGetCharacter(player, targetId, out var userId, out var characterName))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-id");
            return;
        }

        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed) || deed.ShuttleUid is not { Valid: true } shuttleUid)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-ship");
            return;
        }

        EntityUid? stationGrid = null;
        if (_station.GetOwningStation(consoleUid) is { Valid: true } station)
            stationGrid = _station.GetLargestGrid(station);

        if (FindStoreBlocker(shuttleUid, requireDocked: true, stationGrid) is { } blocker)
        {
            Deny(consoleUid, component, player, blocker);
            return;
        }

        var maxShips = _cfg.GetCVar(NFCCVars.BluespaceDockMaxShips);
        if (maxShips > 0 && CountStoredShips(userId, characterName) >= maxShips)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-too-many-ships");
            return;
        }

        var value = AppraiseShip(shuttleUid);
        var cost = (int) (value * _cfg.GetCVar(NFCCVars.BluespaceDockStoreTax));
        if (cost > 0 && (!_bank.TryGetBalance(player, out var balance) || balance < cost))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-insufficient-funds");
            return;
        }

        var displayName = ShipyardSystem.GetFullName(deed);
        if (!TryStoreShip(shuttleUid, userId, characterName, value, player, cost, out var error))
        {
            Deny(consoleUid, component, player, error ?? "bluespace-dock-error-save-failed");
            return;
        }

        Popup(player, "bluespace-dock-stored", ("ship", displayName));
        Announce(consoleUid, "bluespace-dock-announce-store", ("owner", characterName), ("vessel", displayName));
        PlayConfirm(consoleUid, component);
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} stored ship {displayName} ({value} value, {cost} tax) in the bluespace dock via {ToPrettyString(consoleUid)}");
        RefreshState(consoleUid, player);
    }

    /// <summary>
    /// The first thing stopping a ship from being stored, as a localization key (null when it can be
    /// stored). <paramref name="requireDocked"/> is false for the admin force-dock command, which
    /// skips the "docked to this station" requirement but nothing else.
    /// </summary>
    public string? FindStoreBlocker(EntityUid shuttleUid, bool requireDocked, EntityUid? stationGrid)
    {
        if (!Exists(shuttleUid) || !HasComp<ShuttleComponent>(shuttleUid))
            return "bluespace-dock-error-no-ship";

        if (!_cfg.GetCVar(NFCCVars.BluespaceDockEnabled))
            return "bluespace-dock-error-disabled";

        if (_busyShips.Contains(shuttleUid))
            return "bluespace-dock-error-busy";

        if (requireDocked && (stationGrid is not { } grid || !IsDockedToGrid(shuttleUid, grid)))
            return "bluespace-dock-error-not-docked";

        if (FoundOrganics(shuttleUid) is not null)
            return "bluespace-dock-error-organic-aboard";

        if (!TryGetVesselInfo(shuttleUid, out var vesselInfo) || !IsAllowedGroup(vesselInfo.Group))
            return "bluespace-dock-error-not-civilian";

        return null;
    }

    /// <summary>
    /// What a ship is worth, as the dock records it.
    /// </summary>
    public int AppraiseShip(EntityUid shuttleUid)
    {
        return (int) _pricing.AppraiseGrid(shuttleUid, null);
    }

    /// <summary>
    /// The store mechanics, shared by the dock console and the admin force-dock command: strip and
    /// save the ship, optionally charge a payer, write the registry entry, clear any deed pointing at
    /// it and delete the grid. Callers run the checks (<see cref="FindStoreBlocker"/>, ship limits,
    /// affordability) first and handle their own feedback. Returns a localization key in
    /// <paramref name="error"/> when nothing was stored.
    /// </summary>
    public bool TryStoreShip(EntityUid shuttleUid, string ownerUserId, string ownerName, int value,
        EntityUid? payer, int cost, out string? error)
    {
        error = null;

        if (!TryComp<ShuttleDeedComponent>(shuttleUid, out var deed))
        {
            error = "bluespace-dock-error-no-ship";
            return false;
        }

        // The vessel identity lives on the ship's station, which the strip below removes: read it now.
        var known = TryGetVesselInfo(shuttleUid, out var vesselInfo);

        _busyShips.Add(shuttleUid);

        try
        {
            // Anything alive (or dead but revivable) gets off the ship first: neither the saved file
            // nor the grid deletion may swallow passengers or onlookers.
            EvacuateGrid(shuttleUid);

            // Deed names are stored split (base name + suffix) and rebuilt on retrieval, and the base
            // name is normalized: older entries stored the full name as the base name, which made the
            // suffix accumulate on every store/retrieve cycle.
            var baseName = ShipNameHelper.GetBaseName(deed.ShuttleName ?? string.Empty, deed.ShuttleNameSuffix);
            var displayName = ShipNameHelper.BuildFullName(baseName, deed.ShuttleNameSuffix);
            var shipId = Guid.NewGuid().ToString("N");
            var fileName = GetShipFilePath(shipId);

            // The ship's station is a mapped entity that the file cannot carry, so everything pointing
            // at it (and the deed, which lives in the registry) is stripped before the save. The
            // station itself survives until the file is known-good, which keeps a failed store
            // recoverable.
            var shipStation = _station.GetOwningStation(shuttleUid);
            var stripped = new List<(EntityUid Ent, EntityUid Station, bool Member, bool Bound)>();
            UndockShuttle(shuttleUid);

            if (!TrySaveShipFile(shuttleUid, fileName, out error, stripped))
            {
                RestoreStoreFailure(shuttleUid, shipStation, stripped, deed);
                return false;
            }

            if (cost > 0 && payer is { } who && !_bank.TryBankWithdraw(who, cost))
            {
                DeleteShipFile(fileName);
                RestoreStoreFailure(shuttleUid, shipStation, stripped, deed);
                error = "bluespace-dock-error-insufficient-funds";
                return false;
            }

            // The file is good: the ship and its station can go.
            if (shipStation is { Valid: true } ownStation && Exists(ownStation))
                _station.DeleteStation(ownStation);

            AddOrUpdateEntry(new BluespaceDockEntry
            {
                ShipId = shipId,
                File = fileName,
                OwnerUserId = ownerUserId,
                OwnerName = ownerName,
                Vessel = known ? vesselInfo.Vessel?.Id : null,
                Model = known ? vesselInfo.Model : "Custom",
                Name = baseName,
                DisplayName = displayName,
                Suffix = deed.ShuttleNameSuffix,
                Value = value,
                StoredAt = DateTime.UtcNow.ToString("O"),
            });

            // Any deed pointing at the ship goes with it (the owner may be offline, so this is not
            // limited to one ID card).
            ClearDeedsFor(shuttleUid);
            QueueDel(shuttleUid);
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Exception while storing ship {ToPrettyString(shuttleUid)}: {e}");
            error = "bluespace-dock-error-save-failed";
            return false;
        }
        finally
        {
            _busyShips.Remove(shuttleUid);
        }
    }

    /// <summary>
    /// Removes every deed that points at a grid.
    /// </summary>
    private void ClearDeedsFor(EntityUid grid)
    {
        var query = EntityQueryEnumerator<ShuttleDeedComponent>();
        while (query.MoveNext(out var uid, out var deed))
        {
            if (deed.ShuttleUid == grid)
                RemComp<ShuttleDeedComponent>(uid);
        }
    }

    /// <summary>
    /// A player ship in the world (one with a deed): its grid, full name, owner and whether it is
    /// docked. Used by the admin ship manager, which has to work for offline owners too.
    /// </summary>
    public readonly record struct DeedShip(EntityUid Grid, string Name, string Owner, bool Docked);

    /// <summary>
    /// Every player ship in the world, with its deed.
    /// </summary>
    public List<DeedShip> GetDeedShips()
    {
        var ships = new List<DeedShip>();
        var query = EntityQueryEnumerator<ShuttleDeedComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var deed, out var xform))
        {
            if (xform.GridUid != uid)
                continue;

            var docked = false;
            foreach (var dock in _docking.GetDocks(uid))
            {
                if (dock.Comp.DockedWith != null)
                {
                    docked = true;
                    break;
                }
            }

            ships.Add(new DeedShip(uid, ShipyardSystem.GetFullName(deed), deed.ShuttleOwner ?? string.Empty, docked));
        }

        return ships;
    }

    /// <summary>
    /// Deletes a player ship and clears every deed pointing at it, so nothing is left referring to a
    /// grid that no longer exists.
    /// </summary>
    public bool TryDeleteShip(EntityUid grid, out string? error)
    {
        error = null;

        if (!Exists(grid))
        {
            error = "manage-ships-error-missing";
            return false;
        }

        if (_busyShips.Contains(grid))
        {
            error = "bluespace-dock-error-busy";
            return false;
        }

        // Rescue ghosts, passengers and pets before the grid (and everything on it) is deleted.
        EvacuateGrid(grid);

        ClearDeedsFor(grid);

        if (_station.GetOwningStation(grid) is { Valid: true } station)
            _station.DeleteStation(station);

        QueueDel(grid);
        return true;
    }

    /// <summary>
    /// Best-effort undo of a failed store: the ship gets its station references, its deed and its
    /// docking back. Nothing was charged and nothing was deleted.
    /// </summary>
    private void RestoreStoreFailure(EntityUid shuttleUid, EntityUid? station,
        List<(EntityUid Ent, EntityUid Station, bool Member, bool Bound)> stripped, ShuttleDeedComponent deed)
    {
        if (!Exists(shuttleUid))
            return;

        RestoreStationReferences(shuttleUid, stripped);
        _dock.AssignDeed(shuttleUid, shuttleUid, deed.ShuttleName, deed.ShuttleNameSuffix, deed.ShuttleOwner);
        RestoreDocking(shuttleUid, station);
    }

    #endregion Store

    #region Retrieve

    private void OnRetrieveMessage(EntityUid consoleUid, BluespaceDockConsoleComponent component, BluespaceDockRetrieveMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-id");
            return;
        }

        if (!TryGetCharacter(player, targetId, out var userId, out var characterName))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-id");
            return;
        }

        var entry = Registry.Find(args.ShipId);
        if (entry is null || !EntryBelongsToCharacter(entry, userId, characterName))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-entry");
            RefreshState(consoleUid, player);
            return;
        }

        // Self-heal: a registry entry without its ship file is worthless, drop it.
        if (!ShipFileExists(entry.File))
        {
            _sawmill.Warning($"Bluespace dock entry {entry.ShipId} ({entry.DisplayName}) has no ship file, purging it");
            RemoveEntry(entry, deleteFile: false);
            Deny(consoleUid, component, player, "bluespace-dock-error-file-missing");
            RefreshState(consoleUid, player);
            return;
        }

        if (ShipNameInUse(entry.DisplayName))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-name-in-use");
            return;
        }

        if (_station.GetOwningStation(consoleUid) is not { Valid: true } station
            || _station.GetLargestGrid(station) is not { } stationGrid)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-station");
            return;
        }

        var cost = (int) (entry.Value * _cfg.GetCVar(NFCCVars.BluespaceDockRetrieveTax));
        if (!_bank.TryGetBalance(player, out var balance) || balance < cost)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-insufficient-funds");
            return;
        }

        if (!TryLoadShipFile(entry.File, out var shuttleUid) || shuttleUid is not { } grid)
        {
            // Self-heal: an unreadable (corrupt) file is worthless, so drop its entry. A file that
            // parses but fails to load is kept - that can be a transient content error.
            if (!IsShipFileParseable(entry.File))
            {
                _sawmill.Warning($"Bluespace dock entry {entry.ShipId} ({entry.DisplayName}) has a corrupt ship file, purging it");
                RemoveEntry(entry, deleteFile: false);
                Deny(consoleUid, component, player, "bluespace-dock-error-file-corrupt");
            }
            else
            {
                Deny(consoleUid, component, player, "bluespace-dock-error-load-failed");
            }

            RefreshState(consoleUid, player);
            return;
        }

        // The ship now exists in the world. Reserve its grid so no other operation can touch it, and
        // consume the registry entry on any failure past this point: leaving the entry behind while
        // a live copy exists would let the player retrieve the same ship twice.
        _busyShips.Add(grid);

        try
        {
            // Take the tax before the ship is handed over. Without a successful withdrawal the
            // player would either get a free ship or be left with a loaded grid that has no owner.
            if (cost > 0 && !_bank.TryBankWithdraw(player, cost))
            {
                // Delete the freshly loaded grid (and its map) again, or it would sit docked with
                // no deed while the entry is still listed.
                var loadedMapId = Transform(grid).MapID;
                QueueDel(grid);
                if (_map.MapExists(loadedMapId))
                    _map.DeleteMap(loadedMapId);

                Deny(consoleUid, component, player, "bluespace-dock-error-bank-failed");
                return;
            }

            // Give the ship its station and identity back. Older entries have no base name (and may
            // carry a doubled full name), so it is derived and the names are rebuilt from it.
            var baseName = string.IsNullOrEmpty(entry.Name)
                ? ShipNameHelper.GetBaseName(entry.DisplayName, entry.Suffix)
                : entry.Name;
            var displayName = ShipNameHelper.BuildFullName(baseName, entry.Suffix);

            var stationConfig = new StationConfig { StationPrototype = "StandardFrontierVessel" };
            var shipStation = _station.InitializeNewStation(stationConfig, new List<EntityUid> { grid });
            _station.RenameStation(shipStation, displayName, loud: false);
            _metaData.SetEntityName(grid, displayName);

            if (entry.Vessel is { } vesselId && _prototype.HasIndex<VesselPrototype>(vesselId))
            {
                var extra = EnsureComp<ExtraShuttleInformationComponent>(shipStation);
                extra.Vessel = vesselId;
            }

            if (TryComp<ShuttleComponent>(grid, out var shuttleComp))
                _shuttle.TryFTLDock(grid, shuttleComp, stationGrid);

            EnsureComp<Content.Server._NF.GC.Components.DeletionCensusExemptComponent>(grid);
            EnsureComp<LinkedLifecycleGridParentComponent>(grid);

            // A shipyard vessel gets its IFF from its vessel prototype; a registered or hand-built
            // ship needs it too, or the radar/mass scanner draws it without a label.
            _shuttle.AddIFFFlag(grid, IFFFlags.IsPlayerShuttle);
            _shuttle.SetIFFColor(grid, Color.White);

            // Shipyard files carry a warp point, hand-built ships do not, and a file's warp keeps the
            // name it was saved with: make sure there is one and that its name follows the ship.
            EnsureShipWarpPoint(grid, displayName);

            // A ship with no vessel prototype (an admin-restored orphan, for instance) has nothing
            // for the dock's civilian check to look at: give it the registered-vessel marker so it
            // counts as a civilian ship and can be stored again.
            if (entry.Vessel == null)
            {
                var marker = EnsureComp<RegisteredVesselComponent>(grid);
                if (string.IsNullOrEmpty(marker.Model) || marker.Model == "Unknown")
                    marker.Model = string.IsNullOrEmpty(entry.Model) ? "Custom" : entry.Model;
                Dirty(grid, marker);
            }

            // Deed on both the ship and the ID that paid for it (base name + suffix, never the
            // full name as the base name).
            _dock.AssignDeed(grid, grid, baseName, entry.Suffix, entry.OwnerName);
            _dock.AssignDeed(targetId, grid, baseName, entry.Suffix, entry.OwnerName);

            // The owner is the ship's captain: a shipyard purchase writes them a crew record on the
            // ship's station and adds the console's access levels to their ID. Without the record the
            // ship shows up with no crew at all (and no captain), so do the same here.
            if (_player.TryGetSessionByEntity(player, out var ownerSession)
                && _prefs.TryGetCachedPreferences(ownerSession.UserId, out var ownerPrefs)
                && ownerPrefs.SelectedCharacter is HumanoidCharacterProfile ownerProfile
                && TryComp<StationRecordsComponent>(shipStation, out var shipRecords))
            {
                TryComp<FingerprintComponent>(player, out var fingerprint);
                TryComp<DnaComponent>(player, out var dna);

                _records.CreateGeneralRecord(shipStation, targetId, ownerProfile.Name, ownerProfile.Age,
                    ownerProfile.Species, ownerProfile.Gender, "Captain", fingerprint?.Fingerprint,
                    dna?.DNA, ownerProfile, shipRecords);
                _records.Synchronize(shipStation);
            }

            if (TryComp<AccessComponent>(targetId, out var access))
            {
                var tags = access.Tags.ToList();
                tags.AddRange(component.NewAccessLevels);
                _accessSystem.TrySetTags(targetId, tags, access);
            }

            // The ship is a new entity, so give the records console a record for it again (the
            // purchase record points at the deleted original).
            _shuttleRecords.AddRecord(new ShuttleRecord(
                displayName,
                entry.Suffix ?? string.Empty,
                entry.OwnerName,
                GetNetEntity(grid),
                purchasedWithVoucher: false,
                purchasePrice: (uint) Math.Max(entry.Value, 0),
                vesselPrototypeId: entry.Vessel ?? "Custom"));

            RemoveEntry(entry, deleteFile: false);

            Popup(player, "bluespace-dock-retrieved", ("ship", displayName));
            Announce(consoleUid, "bluespace-dock-announce-retrieve", ("owner", entry.OwnerName), ("vessel", displayName));
            PlayConfirm(consoleUid, component);
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(player):player} retrieved ship {displayName} ({cost} tax) from the bluespace dock via {ToPrettyString(consoleUid)}");
        }
        catch (Exception e)
        {
            _sawmill.Error($"Exception while retrieving ship {entry.ShipId}: {e}");

            // The ship (or part of it) is already in the world and the tax is paid; consume the
            // entry so a retry cannot materialize a second copy.
            RemoveEntry(entry, deleteFile: false);
            Deny(consoleUid, component, player, "bluespace-dock-error-load-failed");
        }
        finally
        {
            _busyShips.Remove(grid);
            RefreshState(consoleUid, player);
        }
    }

    #endregion Retrieve

    #region Sell

    private void OnSellStoredMessage(EntityUid consoleUid, BluespaceDockConsoleComponent component, BluespaceDockSellStoredMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-id");
            return;
        }

        if (!TryGetCharacter(player, targetId, out var userId, out var characterName))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-id");
            return;
        }

        var entry = Registry.Find(args.ShipId);
        if (entry is null || !EntryBelongsToCharacter(entry, userId, characterName))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-entry");
            RefreshState(consoleUid, player);
            return;
        }

        if (!ShipFileExists(entry.File))
        {
            _sawmill.Warning($"Bluespace dock entry {entry.ShipId} ({entry.DisplayName}) has no ship file, purging it");
            RemoveEntry(entry, deleteFile: false);
            Deny(consoleUid, component, player, "bluespace-dock-error-file-missing");
            RefreshState(consoleUid, player);
            return;
        }

        var payout = (int) (entry.Value * _cfg.GetCVar(NFCCVars.BluespaceDockStoredSellRate));
        if (!_bank.TryBankDeposit(player, payout))
        {
            // Nothing was paid out and the entry is untouched, so the sale simply does not happen.
            Deny(consoleUid, component, player, "bluespace-dock-error-bank-failed");
            RefreshState(consoleUid, player);
            return;
        }

        RemoveEntry(entry, deleteFile: true);

        Popup(player, "bluespace-dock-sold-stored", ("ship", entry.DisplayName), ("amount", BankSystemExtensions.ToIndependentString(payout)));
        Announce(consoleUid, "bluespace-dock-announce-sell", ("owner", entry.OwnerName), ("vessel", entry.DisplayName), ("player", player));
        PlayConfirm(consoleUid, component);
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} sold stored ship {entry.DisplayName} for {payout} from the bluespace dock via {ToPrettyString(consoleUid)}");
        RefreshState(consoleUid, player);
    }

    private void OnSellActiveMessage(EntityUid consoleUid, BluespaceDockConsoleComponent component, BluespaceDockSellActiveMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-id");
            return;
        }

        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed) || deed.ShuttleUid is not { Valid: true } shuttleUid)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-ship");
            return;
        }

        if (_station.GetOwningStation(consoleUid) is not { Valid: true } stationUid)
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-no-station");
            return;
        }

        if (_shipyard.FindDisableShipyardSaleObjects(shuttleUid, ShipyardConsoleUiKey.Shipyard,
                GetEntityQuery<ShipyardSellConditionComponent>(), GetEntityQuery<TransformComponent>()) is { } blocked)
        {
            Deny(consoleUid, component, player, blocked);
            return;
        }

        // A sale cannot be undone once the shipyard takes the ship, so make sure there is somewhere
        // for the payout to go before starting it.
        if (!_bank.TryGetBalance(player, out _))
        {
            Deny(consoleUid, component, player, "bluespace-dock-error-bank-failed");
            return;
        }

        // The shipyard deletes the sold ship; get its occupants off first so pets, corpses and
        // onlookers are not destroyed along with it.
        EvacuateGrid(shuttleUid);

        // TrySellShuttle refuses every sale while the shipyard's parking map is not set up (it is
        // only created by a purchase or by the CVar handler, which does not run when the shipyard
        // was disabled at round start), so make sure it exists first.
        if (_cfg.GetCVar(NFCCVars.Shipyard))
            _shipyard.SetupShipyardIfNeeded();

        var result = _shipyard.TrySellShuttle(stationUid, shuttleUid, consoleUid, out var bill);
        if (result.Error != ShipyardSystem.ShipyardSaleError.Success)
        {
            var key = result.Error switch
            {
                ShipyardSystem.ShipyardSaleError.Undocked => "shipyard-console-sale-not-docked",
                ShipyardSystem.ShipyardSaleError.OrganicsAboard => "shipyard-console-sale-organic-aboard",
                ShipyardSystem.ShipyardSaleError.InvalidShip => "shipyard-console-sale-invalid-ship",
                _ => "shipyard-console-sale-unknown-reason",
            };
            Deny(consoleUid, component, player, key);
            return;
        }

        var netEntity = GetNetEntity(shuttleUid);
        _shuttleRecords.TrySetSaleTime(netEntity);

        var payout = (int) (bill * _cfg.GetCVar(NFCCVars.BluespaceDockStoredSellRate));
        if (!_bank.TryBankDeposit(player, payout))
        {
            // The shipyard already took the ship; the payout cannot be undone, so record the loss
            // loudly for the admins.
            _sawmill.Error($"Bluespace dock: the shipyard sold {ShipyardSystem.GetFullName(deed)} for {payout} " +
                           $"but the deposit to {ToPrettyString(player)} failed");
        }

        RemComp<ShuttleDeedComponent>(targetId);

        var name = ShipyardSystem.GetFullName(deed);
        Popup(player, "bluespace-dock-sold-active", ("ship", name), ("amount", BankSystemExtensions.ToIndependentString(payout)));
        Announce(consoleUid, "bluespace-dock-announce-sell", ("owner", deed.ShuttleOwner ?? string.Empty), ("vessel", name), ("player", player));
        PlayConfirm(consoleUid, component);
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} sold ship {name} for {payout} via the bluespace dock {ToPrettyString(consoleUid)}");
        RefreshState(consoleUid, player);
    }

    #endregion Sell

    #region Ship state helpers

    private void UndockShuttle(EntityUid shuttleUid)
    {
        foreach (var dock in _docking.GetDocks(shuttleUid))
        {
            if (dock.Comp.DockedWith != null)
                _docking.Undock(dock);

            // Undock() leaves the fields alone when DockedWith is already null, which happens for
            // docks that lost their partner but kept a joint (the shipyard's own undock has the
            // same hole). DockedWith and DockJointId are serialized, so they must not reach the file.
            dock.Comp.DockedWith = null;
            dock.Comp.DockJoint = null;
            dock.Comp.DockJointId = null;
        }
    }

    /// <summary>
    /// Collects a grid and every entity under it: transform children and entities inside containers
    /// (which are detached from the transform tree - a toolbox's contents, for instance).
    /// </summary>
    public void GetEntitiesOnGrid(EntityUid gridUid, HashSet<EntityUid> entities)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        var containerQuery = GetEntityQuery<ContainerManagerComponent>();
        var queue = new Queue<EntityUid>();
        queue.Enqueue(gridUid);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!entities.Add(current))
                continue;

            if (xformQuery.TryGetComponent(current, out var xform))
            {
                var childEnumerator = xform.ChildEnumerator;
                while (childEnumerator.MoveNext(out var child))
                {
                    queue.Enqueue(child);
                }
            }

            if (containerQuery.TryGetComponent(current, out var containers))
            {
                foreach (var container in containers.Containers.Values)
                {
                    foreach (var contained in container.ContainedEntities)
                    {
                        queue.Enqueue(contained);
                    }
                }
            }
        }
    }

    #endregion Ship state helpers

    #region Feedback

    private void Deny(EntityUid consoleUid, BluespaceDockConsoleComponent component, EntityUid player, string messageKey)
    {
        Popup(player, messageKey);
        PlayDeny(consoleUid, component);
    }

    private void Popup(EntityUid player, string messageKey, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(messageKey, args), player, player);
    }

    /// <summary>
    /// Announces a dock action on the console's radio channel (Shortband by default, the same channel
    /// the shipyard console uses) and has the console say it locally, exactly like the shipyard does
    /// for a purchase or a sale.
    /// </summary>
    private void Announce(EntityUid consoleUid, string messageKey, params (string, object)[] args)
    {
        if (!TryComp<BluespaceDockConsoleComponent>(consoleUid, out var component))
            return;

        var text = Loc.GetString(messageKey, args);
        var channel = _prototype.Index(component.AnnounceChannel);

        _radio.SendRadioMessage(consoleUid, text, channel, consoleUid);
        _chat.TrySendInGameICMessage(consoleUid, text, InGameICChatType.Speak, true);
    }

    private void PlayDeny(EntityUid consoleUid, BluespaceDockConsoleComponent component)
    {
        if (_timing.CurTime < component.NextDenySoundTime)
            return;

        component.NextDenySoundTime = _timing.CurTime + component.DenySoundDelay;
        _audio.PlayPvs(component.ErrorSound, consoleUid);
    }

    private void PlayConfirm(EntityUid consoleUid, BluespaceDockConsoleComponent component)
    {
        _audio.PlayPvs(component.ConfirmSound, consoleUid);
    }

    #endregion Feedback
}
