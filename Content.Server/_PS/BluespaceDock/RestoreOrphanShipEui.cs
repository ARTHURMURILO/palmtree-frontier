using Content.Server._PS.BluespaceDock;
using Content.Server._PS.ShipRegistry;
using Content.Server.EUI;
using Content.Shared._PS.BluespaceDock;
using Content.Shared._PS.ShipRegistry;
using Content.Shared.Access.Systems;
using Content.Shared.Eui;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._PS.BluespaceDock;

/// <summary>
/// Palmtree: admin tool for orphaned ship files. Lists the ship files that have no dock entry,
/// previews one on a paused map (the same camera as the ship preview, deleted when the window closes
/// or after a minute as a safety net), restores one into a character's dock or deletes it.
/// </summary>
public sealed partial class RestoreOrphanShipEui : BaseEui
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly BluespaceDockSystem _dock = default!;
    private readonly SharedIdCardSystem _idCards = default!;
    private readonly SharedMapSystem _map = default!;

    /// <summary>How long an abandoned preview grid may live.</summary>
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(1);

    private string? _status;
    private bool _statusIsError;

    private string? _previewFile;
    private EntityUid? _previewGrid;
    private MapId _previewMap;
    private ShipPreviewEui? _previewEui;
    private int _previewGeneration;

    public RestoreOrphanShipEui()
    {
        IoCManager.InjectDependencies(this);

        _dock = _entityManager.System<BluespaceDockSystem>();
        _idCards = _entityManager.System<SharedIdCardSystem>();
        _map = _entityManager.System<SharedMapSystem>();
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        ClearPreview();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case RestoreOrphanShipPreviewMessage preview:
                Preview(preview.File);
                break;
            case RestoreOrphanShipRestoreMessage restore:
                Restore(restore);
                break;
            case RestoreOrphanShipDeleteMessage delete:
                Delete(delete.File);
                break;
        }
    }

    public override EuiStateBase GetNewState()
    {
        var ships = new List<OrphanShipEntry>();
        foreach (var orphan in _dock.GetOrphanedFiles())
        {
            var modified = orphan.Modified?.ToString("yyyy-MM-dd HH:mm") ?? "?";
            ships.Add(new OrphanShipEntry(orphan.Name, modified));
        }

        var players = new List<ShipOwnerOption>();
        foreach (var session in _player.Sessions)
        {
            players.Add(new ShipOwnerOption(session.UserId.ToString(), GetCharacterName(session)));
        }

        return new RestoreOrphanShipEuiState(ships, players, _status, _statusIsError);
    }

    private void Preview(string file)
    {
        ClearPreview();

        if (!_dock.TryLoadOrphanedFile(file, out var grid, out var mapId) || grid is not { } previewGrid)
        {
            SetStatus(Loc.GetString("restore-orphan-error-load-failed"), error: true);
            return;
        }

        _previewFile = file;
        _previewGrid = previewGrid;
        _previewMap = mapId;

        var previewEui = new ShipPreviewEui(previewGrid, ClearPreview);
        _previewEui = previewEui;
        _eui.OpenEui(previewEui, Player);

        // Safety net: a preview whose window never closes (a crashed client, for instance) still
        // goes away.
        var generation = ++_previewGeneration;
        Timer.Spawn(PreviewLifetime, () =>
        {
            if (_previewGeneration != generation)
                return;

            _previewEui?.Close();
            ClearPreview();
        });

        SetStatus(Loc.GetString("restore-orphan-preview-opened", ("file", file)), error: false);
    }

    private void ClearPreview()
    {
        _previewEui = null;
        _previewFile = null;

        if (_previewGrid is { } grid)
        {
            if (_entityManager.EntityExists(grid))
                _entityManager.QueueDeleteEntity(grid);

            _map.DeleteMap(_previewMap);
        }

        _previewGrid = null;
    }

    private void Restore(RestoreOrphanShipRestoreMessage msg)
    {
        // The player picked in the dropdown decides the owner: the character they are playing, taken
        // from the ID card they carry (that is the name the dock keys on).
        if (!TryGetOwner(msg.OwnerUserId, out var ownerName))
        {
            SetStatus(Loc.GetString("restore-orphan-error-no-owner"), error: true);
            return;
        }

        if (!_dock.TryRestoreOrphanedFile(msg.File, msg.OwnerUserId, ownerName, msg.ShipName, msg.Suffix, out var error))
        {
            SetStatus(Loc.GetString(error ?? "restore-orphan-error-unknown"), error: true);
            return;
        }

        if (_previewFile == msg.File)
            ClearPreview();

        SetStatus(Loc.GetString("restore-orphan-restored", ("file", msg.File), ("owner", ownerName)), error: false);
        StateDirty();
    }

    /// <summary>
    /// The character name an entry for this player will be keyed on.
    /// </summary>
    private bool TryGetOwner(string userId, out string characterName)
    {
        characterName = string.Empty;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var guid))
            return false;

        if (!_player.TryGetSessionById(new NetUserId(guid), out var session))
            return false;

        characterName = GetCharacterName(session);
        return !string.IsNullOrWhiteSpace(characterName);
    }

    private void Delete(string file)
    {
        if (!_dock.TryDeleteOrphanedFile(file))
        {
            SetStatus(Loc.GetString("restore-orphan-error-delete-refused"), error: true);
            return;
        }

        if (_previewFile == file)
            ClearPreview();

        SetStatus(Loc.GetString("restore-orphan-deleted", ("file", file)), error: false);
        StateDirty();
    }

    private void SetStatus(string status, bool error)
    {
        _status = status;
        _statusIsError = error;
        StateDirty();
    }

    /// <summary>
    /// The character name the dock will key the entry on. The player's profile comes first (no ID
    /// card needed), then the ID they carry, then their mob's name.
    /// </summary>
    private string GetCharacterName(ICommonSession session)
    {
        string? idName = null;
        if (session.AttachedEntity is { } mob && _idCards.TryFindIdCard(mob, out var idCard))
            idName = idCard.Comp.FullName;

        return _dock.GetCharacterName(session.UserId, idName, session.AttachedEntity);
    }
}
