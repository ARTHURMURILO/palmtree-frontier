using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._PS.BluespaceDock;
using Content.Server._NF.GC.Components;
using Content.Server._NF.ShuttleRecords;
using Content.Server.StationEvents.Components;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Cargo.Systems;
using Content.Server.EUI;
using Content.Server.Popups;
using Content.Server.Shuttles.Systems;
using Content.Server.StationEvents.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.CCVar;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared._NF.Shipyard;
using Content.Shared._PS.BluespaceDock;
using Content.Shared._PS.ShipRegistry;
using Content.Shared.Access.Components;
using Content.Shared.Administration;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Station;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipRegistry;

/// <summary>
/// Palmtree: Ship Registrar. Registers a docked, unregistered grid (a hand-built ship) as a
/// civilian vessel: it gets a station, a deed (on the ship and on the owner's ID), a shuttle
/// record, an IFF name and a <see cref="RegisteredVesselComponent"/>, which is what the Bluespace
/// Dock's civilian check accepts. Requires the Station Representative's ID in the privileged slot
/// (admins bypass) and the owner's ID in the target slot.
/// </summary>
public sealed class ShipRegistrarSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedBluespaceDockSystem _dock = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly ShuttleRecordsSystem _shuttleRecords = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly BluespaceDockSystem _dockSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    /// <summary>
    /// Station prototype registered ships use (the same one shipyard ships get).
    /// </summary>
    public const string RegisteredStationPrototype = "StandardFrontierVessel";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipRegistrarConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<ShipRegistrarConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<ShipRegistrarConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<ShipRegistrarConsoleComponent, ShipRegistrarRegisterMessage>(OnRegisterMessage);
        SubscribeLocalEvent<ShipRegistrarConsoleComponent, ShipRegistrarPreviewMessage>(OnPreviewMessage);
    }

    #region UI

    private void OnConsoleOpened(EntityUid uid, ShipRegistrarConsoleComponent component, BoundUIOpenedEvent args)
    {
        RefreshState(uid, args.Actor);
    }

    private void OnItemSlotChanged(EntityUid uid, ShipRegistrarConsoleComponent component, ContainerModifiedMessage args)
    {
        if (args.Container.ID != component.PrivilegedIdSlot.ID && args.Container.ID != component.TargetIdSlot.ID)
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
        var component = Comp<ShipRegistrarConsoleComponent>(consoleUid);

        var privilegedId = component.PrivilegedIdSlot.ContainerSlot?.ContainedEntity;
        var targetId = component.TargetIdSlot.ContainerSlot?.ContainedEntity;

        string? privilegedName = null;
        string? targetName = null;
        if (privilegedId is { Valid: true } && TryComp<IdCardComponent>(privilegedId, out var privilegedCard))
            privilegedName = privilegedCard.FullName;
        if (targetId is { Valid: true } && TryComp<IdCardComponent>(targetId, out var targetCard))
            targetName = targetCard.FullName;

        _bank.TryGetBalance(actor, out var balance);

        var grids = GetDockableGrids(consoleUid);

        _ui.SetUiState(consoleUid, ShipRegistrarUiKey.Key, new ShipRegistrarUiState(
            privilegedId is { Valid: true },
            privilegedName,
            targetId is { Valid: true },
            targetName,
            balance,
            _cfg.GetCVar(NFCCVars.ShipRegistrarFee),
            grids));
    }

    /// <summary>
    /// Every grid docked to this console's station that is not registered yet (no deed and no
    /// registered-vessel marker).
    /// </summary>
    public List<ShipRegistrarGridUi> GetDockableGrids(EntityUid consoleUid)
    {
        var result = new List<ShipRegistrarGridUi>();

        if (_station.GetOwningStation(consoleUid) is not { Valid: true } station
            || !TryComp<StationDataComponent>(station, out var stationData))
        {
            return result;
        }

        var stationGrids = stationData.Grids.ToList();
        if (stationGrids.Count == 0)
            return result;

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var gridUid, out _, out _))
        {
            if (stationGrids.Contains(gridUid))
                continue;

            if (_station.GetOwningStation(gridUid) is not null)
                continue;

            if (HasComp<Content.Shared._NF.Shipyard.Components.ShuttleDeedComponent>(gridUid)
                || HasComp<RegisteredVesselComponent>(gridUid))
            {
                continue;
            }

            if (!IsDockedToAny(gridUid, stationGrids))
                continue;

            var name = MetaData(gridUid).EntityName;
            var size = _physics.GetWorldAABB(gridUid).Size;
            var value = (int) _pricing.AppraiseGrid(gridUid, null);

            result.Add(new ShipRegistrarGridUi(
                GetNetEntity(gridUid),
                name,
                $"{size.X:0.#} x {size.Y:0.#}",
                value));
        }

        return result;
    }

    #endregion UI

    #region Registration

    private void OnRegisterMessage(EntityUid consoleUid, ShipRegistrarConsoleComponent component, ShipRegistrarRegisterMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.PrivilegedIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } privilegedId
            || !IsPrivilegedAuthorized(consoleUid, component, player, privilegedId))
        {
            Deny(consoleUid, component, player, "ship-registrar-error-privileged-id");
            return;
        }

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            Deny(consoleUid, component, player, "ship-registrar-error-target-id");
            return;
        }

        if (!TryGetEntity(args.Grid, out var gridUid) || gridUid is not { } grid)
        {
            Deny(consoleUid, component, player, "ship-registrar-error-no-grid");
            RefreshState(consoleUid, player);
            return;
        }

        if (!TryRegisterVessel(consoleUid, player, targetId, grid, args.Name, args.Suffix, out var error))
        {
            Deny(consoleUid, component, player, error ?? "ship-registrar-error-unknown");
            RefreshState(consoleUid, player);
            return;
        }

        var fullName = ShipNameHelper.BuildFullName(args.Name, args.Suffix);
        Popup(player, "ship-registrar-registered", ("ship", fullName));
        _audio.PlayPvs(component.ConfirmSound, consoleUid);
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} registered grid {fullName} as a civilian vessel via {ToPrettyString(consoleUid)}");
        RefreshState(consoleUid, player);
    }

    /// <summary>
    /// Registers a grid as a civilian vessel. All checks are done here (the UI is only a hint), and
    /// the fee is charged only after the ship has been set up.
    /// </summary>
    public bool TryRegisterVessel(
        EntityUid consoleUid,
        EntityUid actor,
        EntityUid targetId,
        EntityUid grid,
        string name,
        string suffix,
        out string? error)
    {
        error = null;

        name = Sanitize(name, ShipRegistrarConsoleComponent.MaxNameLength);
        suffix = Sanitize(suffix, ShipRegistrarConsoleComponent.MaxSuffixLength);

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "ship-registrar-error-bad-name";
            return false;
        }

        if (!TryComp<IdCardComponent>(targetId, out var targetCard))
        {
            error = "ship-registrar-error-target-id";
            return false;
        }

        if (_station.GetOwningStation(consoleUid) is not { Valid: true } station
            || !TryComp<StationDataComponent>(station, out var stationData)
            || stationData.Grids.Count == 0)
        {
            error = "ship-registrar-error-no-station";
            return false;
        }

        if (!IsDockedToAny(grid, stationData.Grids.ToList()))
        {
            error = "ship-registrar-error-not-docked";
            return false;
        }

        if (HasComp<Content.Shared._NF.Shipyard.Components.ShuttleDeedComponent>(grid)
            || HasComp<RegisteredVesselComponent>(grid))
        {
            error = "ship-registrar-error-already-registered";
            return false;
        }

        if (_station.GetOwningStation(grid) is not null)
        {
            error = "ship-registrar-error-already-registered";
            return false;
        }

        var fee = _cfg.GetCVar(NFCCVars.ShipRegistrarFee);
        if (fee > 0 && (!_bank.TryGetBalance(actor, out var balance) || balance < fee))
        {
            error = "ship-registrar-error-insufficient-funds";
            return false;
        }

        var fullName = ShipNameHelper.BuildFullName(name, suffix);
        var ownerName = targetCard.FullName ?? string.Empty;

        // Station first: InitializeNewStation also stamps the grid's station membership.
        var config = new StationConfig { StationPrototype = RegisteredStationPrototype };
        var shipStation = _station.InitializeNewStation(config, new List<EntityUid> { grid }, fullName);
        _metaData.SetEntityName(grid, fullName);

        var registered = EnsureComp<RegisteredVesselComponent>(grid);
        registered.Group = ShipyardConsoleUiKey.Shipyard;
        registered.Model = "Custom";
        Dirty(grid, registered);

        // Same IFF every shipyard vessel gets (BaseVessel: IsPlayerShuttle, white). Without an
        // IFFComponent the radar and mass scanner refuse to draw a label for the grid, so a
        // registered ship used to be an anonymous contact.
        _shuttle.AddIFFFlag(grid, IFFFlags.IsPlayerShuttle);
        _shuttle.SetIFFColor(grid, Color.White);

        // Deed on the ship and on the owner's ID.
        _dock.AssignDeed(grid, grid, name, suffix, ownerName);
        _dock.AssignDeed(targetId, grid, name, suffix, ownerName);

        // A warp point, like shipyard ships carry in their files: it makes the ship reachable with
        // `warp` and in the ghost warp menu, and its name follows the ship's.
        _dockSystem.EnsureShipWarpPoint(grid, fullName);

        // Keep the ship alive through station events / garbage collection, like a shipyard purchase.
        EnsureComp<DeletionCensusExemptComponent>(grid);
        EnsureComp<LinkedLifecycleGridParentComponent>(grid);

        _shuttleRecords.AddRecord(new ShuttleRecord(
            name,
            suffix,
            ownerName,
            GetNetEntity(grid),
            purchasedWithVoucher: false,
            purchasePrice: 0,
            vesselPrototypeId: "Custom"));

        if (fee > 0)
            _bank.TryBankWithdraw(actor, fee);

        _ = shipStation;
        return true;
    }

    private static string Sanitize(string value, int maxLength)
    {
        value = value.Trim().Replace('\n', ' ').Replace('\r', ' ');
        if (value.Length > maxLength)
            value = value[..maxLength];
        return value;
    }

    /// <summary>
    /// The privileged ID must satisfy the console's access reader (Station Representative access);
    /// admins may use any ID.
    /// </summary>
    private bool IsPrivilegedAuthorized(EntityUid consoleUid, ShipRegistrarConsoleComponent component, EntityUid actor, EntityUid privilegedId)
    {
        if (_admin.HasAdminFlag(actor, AdminFlags.Admin))
            return true;

        if (!TryComp<AccessReaderComponent>(consoleUid, out var reader))
            return false;

        return _accessReader.IsAllowed(privilegedId, consoleUid, reader);
    }

    #endregion Registration

    #region Preview

    private void OnPreviewMessage(EntityUid consoleUid, ShipRegistrarConsoleComponent component, ShipRegistrarPreviewMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryGetEntity(args.Grid, out var gridUid) || gridUid is not { } grid)
        {
            Deny(consoleUid, component, player, "ship-registrar-error-no-grid");
            return;
        }

        if (!_player.TryGetSessionByEntity(player, out var session))
            return;

        _eui.OpenEui(new ShipPreviewEui(grid), session);
    }

    #endregion Preview

    #region Checks

    /// <summary>
    /// Whether the grid has a dock joined to any of the given grids.
    /// </summary>
    private bool IsDockedToAny(EntityUid gridUid, List<EntityUid> otherGrids)
    {
        var docks = _docking.GetDocks(gridUid);

        foreach (var dock in docks)
        {
            if (dock.Comp.DockedWith is not { Valid: true } dockedWith)
                continue;

            if (otherGrids.Contains(Transform(dockedWith).GridUid ?? EntityUid.Invalid))
                return true;
        }

        return false;
    }

    #endregion Checks

    #region Feedback

    private void Deny(EntityUid consoleUid, ShipRegistrarConsoleComponent component, EntityUid player, string messageKey)
    {
        Popup(player, messageKey);

        if (_timing.CurTime < component.NextDenySoundTime)
            return;

        component.NextDenySoundTime = _timing.CurTime + component.DenySoundDelay;
        _audio.PlayPvs(component.ErrorSound, consoleUid);
    }

    private void Popup(EntityUid player, string messageKey, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(messageKey, args), player, player);
    }

    #endregion Feedback
}
