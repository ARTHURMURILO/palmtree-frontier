using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Server.Shuttles.Systems;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: the engineering shielding console device. It edits the grid's <see cref="ShipCombatComponent"/>
/// (condition and power pools), drives shield redistribution and shows the system alerts.
/// </summary>
public sealed class ShipShieldingConsoleSystem : EntitySystem
{
    [Dependency] private readonly ShipCombatSystem _combat = default!;
    [Dependency] private readonly ShipPowerSystem _power = default!;
    [Dependency] private readonly ShipShieldSystem _shields = default!;
    [Dependency] private readonly ShuttleConsoleSystem _shuttleConsole = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>How often open windows refresh their live shield values.</summary>
    private const float RefreshInterval = 1f;

    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipShieldingConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipShieldingConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ShipShieldingConsoleComponent, ShipConsoleSetConditionMessage>(OnSetCondition);
        SubscribeLocalEvent<ShipShieldingConsoleComponent, ShieldingConsoleSetPoolMessage>(OnSetPool);
        SubscribeLocalEvent<ShipShieldingConsoleComponent, ShieldingConsoleSetPoolLockMessage>(OnSetPoolLock);
        SubscribeLocalEvent<ShipShieldingConsoleComponent, ShieldingConsoleSetPresetMessage>(OnSetPreset);
        SubscribeLocalEvent<ShipShieldingConsoleComponent, ShieldingConsoleRedistributeMessage>(OnRedistribute);
        SubscribeLocalEvent<ShipShieldingConsoleComponent, ShieldingConsoleSetSilentMessage>(OnSetSilent);
        SubscribeLocalEvent<ShipCombatConditionChangedEvent>(OnConditionChanged);
        SubscribeLocalEvent<ShipCombatStateChangedEvent>(OnStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + TimeSpan.FromSeconds(RefreshInterval);

        // Live shield values: refresh the open windows once a second.
        var query = EntityQueryEnumerator<ShipShieldingConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (_ui.IsUiOpen(uid, ShieldingConsoleUiKey.Key))
                UpdateState(uid, console);
        }
    }

    private void OnMapInit(EntityUid uid, ShipShieldingConsoleComponent component, MapInitEvent args)
    {
        _combat.TryEnsureShipCombat(uid, out _);
        UpdateState(uid, component);
    }

    private void OnUiOpened(EntityUid uid, ShipShieldingConsoleComponent component, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not ShieldingConsoleUiKey.Key)
            return;

        UpdateState(uid, component);
    }

    private void OnSetCondition(EntityUid uid, ShipShieldingConsoleComponent component, ShipConsoleSetConditionMessage args)
    {
        _combat.TrySetCondition(uid, args.Condition);
    }

    private void OnSetSilent(EntityUid uid, ShipShieldingConsoleComponent component, ShieldingConsoleSetSilentMessage args)
    {
        if (component.Silent == args.Silent)
            return;

        component.Silent = args.Silent;
        UpdateState(uid, component);
    }

    private void OnSetPool(EntityUid uid, ShipShieldingConsoleComponent component, ShieldingConsoleSetPoolMessage args)
    {
        if (!_combat.TryGetShipCombat(uid, out var combat))
            return;

        if (!Enum.IsDefined(args.Pool))
            return;

        _power.SetPool(combat, args.Pool, args.Value);
        UpdateState(uid, component);
    }

    private void OnSetPoolLock(EntityUid uid, ShipShieldingConsoleComponent component, ShieldingConsoleSetPoolLockMessage args)
    {
        if (!_combat.TryGetShipCombat(uid, out var combat) || !Enum.IsDefined(args.Pool))
            return;

        ShipPowerSystem.SetPoolLocked(combat.Comp, args.Pool, args.Locked);
        UpdateState(uid, component);
    }

    private void OnSetPreset(EntityUid uid, ShipShieldingConsoleComponent component, ShieldingConsoleSetPresetMessage args)
    {
        if (!_combat.TryGetShipCombat(uid, out var combat) || !Enum.IsDefined(args.Preset))
            return;

        _power.ApplyPreset(combat, args.Preset);
        UpdateState(uid, component);
    }

    private void OnRedistribute(EntityUid uid, ShipShieldingConsoleComponent component, ShieldingConsoleRedistributeMessage args)
    {
        if (!Enum.IsDefined(args.Facing))
            return;

        _shields.TryRedistribute(uid, args.Facing);
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

    private void UpdateConsolesOnGrid(EntityUid grid)
    {
        var consoles = EntityQueryEnumerator<ShipShieldingConsoleComponent, TransformComponent>();
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
    public void UpdateState(EntityUid uid, ShipShieldingConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!_ui.HasUi(uid, ShieldingConsoleUiKey.Key))
            return;

        if (!_combat.TryGetShipCombat(uid, out var combat))
            return;

        _ui.SetUiState(uid, ShieldingConsoleUiKey.Key, BuildState(uid, combat.Comp));
    }

    /// <summary>
    /// Builds the window state for a console.
    /// </summary>
    public ShieldingConsoleBoundUserInterfaceState BuildState(EntityUid uid, ShipCombatComponent combat)
    {
        var docks = _shuttleConsole.GetAllDocks();
        var nav = _shuttleConsole.GetNavState(uid, docks);

        TryComp<ShipShieldComponent>(combat.Owner, out var shield);

        var cooldown = shield != null && _timing.CurTime < shield.NextRedistribution
            ? (float) (shield.NextRedistribution - _timing.CurTime).TotalSeconds
            : 0f;

        return new ShieldingConsoleBoundUserInterfaceState(
            nav,
            combat.Condition,
            combat.ShieldPower,
            combat.WeaponPower,
            combat.ThrusterPower,
            combat.AuxiliaryPower,
            combat.ShieldPowerLocked,
            combat.WeaponPowerLocked,
            combat.ThrusterPowerLocked,
            combat.AuxiliaryPowerLocked,
            shield?.Facings ?? new float[4],
            shield?.Up ?? new[] { false, false, false, false },
            _shields.GetMaxPerFacing(combat.Owner),
            cooldown,
            shieldsAlert: GetShieldsAlert(combat),
            weaponsAlert: GetWeaponsAlert(combat),
            thrustersAlert: GetThrustersAlert(combat.Owner),
            auxiliaryAlert: GetAuxiliaryAlert(combat),
            silent: TryComp<ShipShieldingConsoleComponent>(uid, out var console) && console.Silent);
    }

    /// <summary>
    /// Auxiliary alert: the ship's grid load exceeds what the sink can supply (like the power
    /// monitoring console). A ship whose grid draws less than its share does not alert, even at a
    /// low Auxiliary setting.
    /// </summary>
    private bool GetAuxiliaryAlert(ShipCombatComponent combat)
    {
        var query = EntityQueryEnumerator<BluespacePowerSinkComponent, PowerSupplierComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var supplier, out var xform))
        {
            if (xform.GridUid != combat.Owner)
                continue;

            if (supplier.Net is PowerNet net && net.NetworkNode.LastCombinedLoad > supplier.MaxSupply + 1f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Shields alert: fitted shields whose pool is below the minimum, or an unpowered generator.
    /// </summary>
    private bool GetShieldsAlert(ShipCombatComponent combat)
    {
        if (!HasComp<ShipShieldComponent>(combat.Owner))
            return false;

        if (ShipPowerSystem.GetPool(combat, ShipPowerPool.Shields) < ShipPowerSystem.MinOperatingPool)
            return true;

        return HasUnpoweredDevices(combat.Owner, ShipPowerPool.Shields);
    }

    /// <summary>
    /// Weapons alert: any weapon device unpowered, or a weapon pool below the minimum.
    /// </summary>
    private bool GetWeaponsAlert(ShipCombatComponent combat)
    {
        var hasWeapon = false;

        var query = EntityQueryEnumerator<BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out _, out var powered, out var xform))
        {
            if (xform.GridUid != combat.Owner || powered.Pool != ShipPowerPool.Weapons)
                continue;

            hasWeapon = true;
            if (!powered.Powered)
                return true;
        }

        return hasWeapon && ShipPowerSystem.GetPool(combat, ShipPowerPool.Weapons) < ShipPowerSystem.MinOperatingPool;
    }

    /// <summary>
    /// Thrusters alert: no bluespace thrusters, or one of them unpowered.
    /// </summary>
    private bool GetThrustersAlert(EntityUid grid)
    {
        var hasThruster = false;

        var query = EntityQueryEnumerator<BluespaceThrusterComponent, BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var powered, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            hasThruster = true;
            if (!powered.Powered)
                return true;
        }

        return !hasThruster;
    }

    private bool HasUnpoweredDevices(EntityUid grid, ShipPowerPool pool)
    {
        var query = EntityQueryEnumerator<BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out _, out var powered, out var xform))
        {
            if (xform.GridUid == grid && powered.Pool == pool && !powered.Powered)
                return true;
        }

        return false;
    }
}
