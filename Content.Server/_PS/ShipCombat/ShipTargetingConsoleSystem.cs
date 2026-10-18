using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: the ship targeting ("tactical") console device. It renders the shuttle radar view with
/// tactical overlays, edits the grid's <see cref="ShipCombatComponent"/> and plays the contact
/// alerts. Locking and firing live in the grid-level targeting/weapon systems so a future AI brain
/// can drive them without a console.
/// </summary>
public sealed class ShipTargetingConsoleSystem : EntitySystem
{
    [Dependency] private readonly ShipCombatSystem _combat = default!;
    [Dependency] private readonly ShipTargetingSystem _targeting = default!;
    [Dependency] private readonly ShuttleConsoleSystem _shuttleConsole = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>How often consoles rescan the grids in their range.</summary>
    private const float ContactScanInterval = 1f;

    /// <summary>Grids each console could see on the previous scan (runtime only).</summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _contacts = new();

    /// <summary>Next time each console may play a contact sound.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextContactSound = new();

    private TimeSpan _nextContactScan;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipTargetingConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, TacticalConsoleSetConditionMessage>(OnSetCondition);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, TacticalConsoleSetAutomaticConditionMessage>(OnSetAutomaticCondition);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, TacticalConsoleSetAutomaticFireMessage>(OnSetAutomaticFire);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, TacticalConsoleSetAutomaticRetaliationMessage>(OnSetAutomaticRetaliation);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, TacticalConsoleSetWeaponModeMessage>(OnSetWeaponMode);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, TacticalConsoleLockRequestMessage>(OnLockRequest);
        SubscribeLocalEvent<ShipTargetingConsoleComponent, TacticalConsoleClearLockMessage>(OnClearLock);
        SubscribeLocalEvent<ShipCombatConditionChangedEvent>(OnConditionChanged);
        SubscribeLocalEvent<ShipCombatStateChangedEvent>(OnStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextContactScan)
            return;

        _nextContactScan = _timing.CurTime + TimeSpan.FromSeconds(ContactScanInterval);

        // Grid positions are shared between every console, so collect them once per scan.
        var grids = new List<(EntityUid Grid, MapId Map, Vector2 Position)>();
        var gridQuery = EntityQueryEnumerator<MapGridComponent, PhysicsComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out var grid, out var gridPhysics, out var gridXform))
        {
            var position = _transform.ToMapCoordinates(new EntityCoordinates(gridUid, gridPhysics.LocalCenter)).Position;
            grids.Add((gridUid, gridXform.MapID, position));
        }

        // Only grids with a shuttle console count as ships for contact alerts. Asteroids, wrecks
        // and other non-ship grids do not beep (they are still lockable).
        var shipGrids = new HashSet<EntityUid>();
        var shuttleConsoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (shuttleConsoles.MoveNext(out _, out _, out var shuttleXform))
        {
            if (shuttleXform.GridUid is { } shuttleGrid)
                shipGrids.Add(shuttleGrid);
        }

        var consoles = EntityQueryEnumerator<ShipTargetingConsoleComponent, RadarConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var radar, out var xform))
        {
            ScanContacts(uid, console, radar, xform, grids, shipGrids);
        }
    }

    private void OnMapInit(EntityUid uid, ShipTargetingConsoleComponent component, MapInitEvent args)
    {
        // A console on a grid turns the grid into a ship with combat state.
        _combat.TryEnsureShipCombat(uid, out _);
        UpdateState(uid, component);
    }

    private void OnShutdown(EntityUid uid, ShipTargetingConsoleComponent component, ComponentShutdown args)
    {
        _contacts.Remove(uid);
        _nextContactSound.Remove(uid);
    }

    private void OnUiOpened(EntityUid uid, ShipTargetingConsoleComponent component, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not TacticalConsoleUiKey.Key)
            return;

        UpdateState(uid, component);
    }

    private void OnSetCondition(EntityUid uid, ShipTargetingConsoleComponent component, TacticalConsoleSetConditionMessage args)
    {
        // The condition-changed event refreshes every console on the grid.
        _combat.TrySetCondition(uid, args.Condition);
    }

    private void OnSetAutomaticCondition(EntityUid uid, ShipTargetingConsoleComponent component, TacticalConsoleSetAutomaticConditionMessage args)
    {
        if (!_combat.TryGetShipCombat(uid, out var combat) || combat.Comp.AutomaticCondition == args.Enabled)
            return;

        combat.Comp.AutomaticCondition = args.Enabled;
        UpdateState(uid, component);
    }

    private void OnSetAutomaticFire(EntityUid uid, ShipTargetingConsoleComponent component, TacticalConsoleSetAutomaticFireMessage args)
    {
        if (!_combat.TryGetShipCombat(uid, out var combat) || combat.Comp.AutomaticFire == args.Enabled)
            return;

        combat.Comp.AutomaticFire = args.Enabled;
        UpdateState(uid, component);
    }

    private void OnSetAutomaticRetaliation(EntityUid uid, ShipTargetingConsoleComponent component, TacticalConsoleSetAutomaticRetaliationMessage args)
    {
        if (!_combat.TryGetShipCombat(uid, out var combat) || combat.Comp.AutomaticRetaliation == args.Enabled)
            return;

        combat.Comp.AutomaticRetaliation = args.Enabled;
        UpdateState(uid, component);
    }

    private void OnSetWeaponMode(EntityUid uid, ShipTargetingConsoleComponent component, TacticalConsoleSetWeaponModeMessage args)
    {
        if (!Enum.IsDefined(args.Mode))
            return;

        if (!_combat.TryGetShipCombat(uid, out var combat) || combat.Comp.WeaponMode == args.Mode)
            return;

        combat.Comp.WeaponMode = args.Mode;
        UpdateState(uid, component);
    }

    private void OnConditionChanged(ShipCombatConditionChangedEvent args)
    {
        UpdateConsolesOnGrid(args.Grid);
    }

    private void OnStateChanged(ShipCombatStateChangedEvent args)
    {
        UpdateConsolesOnGrid(args.Grid);
    }

    private void OnLockRequest(EntityUid uid, ShipTargetingConsoleComponent component, TacticalConsoleLockRequestMessage args)
    {
        if (!EntityManager.TryGetEntity(args.Target, out var target) || target is not { } targetGrid)
            return;

        if (!TryComp<RadarConsoleComponent>(uid, out var radar))
            return;

        _targeting.TryStartLock(uid, targetGrid, radar.MaxRange);
    }

    private void OnClearLock(EntityUid uid, ShipTargetingConsoleComponent component, TacticalConsoleClearLockMessage args)
    {
        _targeting.TryClearLock(uid);
    }

    /// <summary>
    /// Refreshes the window of every tactical console on a grid.
    /// </summary>
    private void UpdateConsolesOnGrid(EntityUid grid)
    {
        var consoles = EntityQueryEnumerator<ShipTargetingConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            UpdateState(uid, console);
        }
    }

    /// <summary>
    /// Sends the current console state to the window.
    /// </summary>
    public void UpdateState(EntityUid uid, ShipTargetingConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!_ui.HasUi(uid, TacticalConsoleUiKey.Key))
            return;

        if (!_combat.TryGetShipCombat(uid, out var combat))
            return;

        _ui.SetUiState(uid, TacticalConsoleUiKey.Key, BuildState(uid, combat.Comp));
    }

    /// <summary>
    /// Builds the window state for a console.
    /// </summary>
    public TacticalConsoleBoundUserInterfaceState BuildState(EntityUid uid, ShipCombatComponent combat)
    {
        var docks = _shuttleConsole.GetAllDocks();
        var nav = _shuttleConsole.GetNavState(uid, docks);

        var hasLock = combat.LockTarget != null && !combat.LockInProgress;

        return new TacticalConsoleBoundUserInterfaceState(
            nav,
            combat.Condition,
            combat.WeaponMode,
            combat.AutomaticCondition,
            combat.AutomaticFire,
            combat.AutomaticRetaliation,
            beingLocked: combat.IncomingLocks.Count > 0,
            incomingMissile: false, // M8: torpedo alarms
            hasLock: hasLock,
            lockTarget: hasLock ? GetNetEntity(combat.LockTarget) : null);
    }

    /// <summary>
    /// The grids a console could see on its last scan, or null when it has not scanned yet.
    /// </summary>
    public HashSet<EntityUid>? GetContacts(EntityUid uid)
    {
        return _contacts.GetValueOrDefault(uid);
    }

    private void ScanContacts(
        EntityUid uid,
        ShipTargetingConsoleComponent console,
        RadarConsoleComponent radar,
        TransformComponent xform,
        List<(EntityUid Grid, MapId Map, Vector2 Position)> grids,
        HashSet<EntityUid> shipGrids)
    {
        if (xform.GridUid is not { } ownGrid)
            return;

        var consolePosition = _transform.GetMapCoordinates(xform).Position;
        var range = radar.MaxRange;

        var found = new HashSet<EntityUid>();
        foreach (var (grid, map, position) in grids)
        {
            if (grid == ownGrid || map != xform.MapID)
                continue;

            if (!shipGrids.Contains(grid))
                continue;

            if ((position - consolePosition).Length() > range)
                continue;

            found.Add(grid);
        }

        if (!_contacts.TryGetValue(uid, out var previous))
        {
            _contacts[uid] = found;
            return;
        }

        var entered = false;
        foreach (var grid in found)
        {
            if (previous.Contains(grid))
                continue;

            entered = true;
            break;
        }

        var left = false;
        foreach (var grid in previous)
        {
            if (found.Contains(grid))
                continue;

            left = true;
            break;
        }

        _contacts[uid] = found;

        if (!entered && !left)
            return;

        if (_nextContactSound.TryGetValue(uid, out var next) && _timing.CurTime < next)
            return;

        // The alert is played at the console, audible by anyone nearby - it does not need the
        // window to be open.
        _audio.PlayPvs(entered ? console.ContactEnterSound : console.ContactLeaveSound, uid);
        _nextContactSound[uid] = _timing.CurTime + console.ContactSoundCooldown;
    }
}
