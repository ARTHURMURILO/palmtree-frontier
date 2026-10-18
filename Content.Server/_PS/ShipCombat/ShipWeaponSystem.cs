using System.Numerics;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared._PS.Weapons;
using Content.Shared.DeviceLinking;
using Content.Shared.Maps;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: fires the ship's console-controlled weapons.
/// </summary>
/// <remarks>
/// Weapons are grouped by the device-link sink port they are linked on (Primary/Secondary/Aux).
/// Turrets rotate towards the locked target inside their 45-degree arc (and sweep idly otherwise)
/// and fire once aligned; stationary weapons fire straight along their facing when the target is
/// roughly ahead and the muzzle is exposed to space. Damage scales with the Weapons power pool
/// through <see cref="GunDamageMultiplierComponent"/>; unpowered weapons cannot fire.
/// </remarks>
public sealed class ShipWeaponSystem : EntitySystem
{
    /// <summary>Sink port id to weapon group.</summary>
    private static readonly Dictionary<string, ShipWeaponMode> PortModes = new()
    {
        ["ShipWeaponPrimary"] = ShipWeaponMode.Primary,
        ["ShipWeaponSecondary"] = ShipWeaponMode.Secondary,
        ["ShipWeaponAux"] = ShipWeaponMode.Aux,
    };

    [Dependency] private readonly ShipCombatSystem _combat = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipWeaponComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, ShipWeaponComponent component, MapInitEvent args)
    {
        _combat.TryEnsureShipCombat(uid, out _);
        component.BaseAngle = Transform(uid).LocalRotation;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipCombatComponent>();
        while (query.MoveNext(out var grid, out var combat))
        {
            UpdateGrid(grid, combat, frameTime);
        }
    }

    /// <summary>
    /// Fires the ship's selected weapon group once (the console's MANUAL FIRE button). Turrets only
    /// fire when the target is inside their arc; fixed weapons when the target is ahead and the
    /// muzzle is exposed.
    /// </summary>
    public bool TryManualFire(EntityUid console)
    {
        if (!_combat.TryGetShipCombat(console, out var combat))
            return false;

        if (combat.Comp.Condition == ShipCondition.Green)
            return false;

        var grid = combat.Owner;
        var target = GetTargetCoordinates(grid, combat.Comp);
        if (target == null)
            return false;

        var groups = BuildGroups(grid);
        var fired = false;

        var query = EntityQueryEnumerator<ShipWeaponComponent, BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var weapon, out var powered, out var xform))
        {
            if (xform.GridUid != grid || !powered.Powered)
                continue;

            if (!IsArmed(combat.Comp.WeaponMode, weapon, groups.GetValueOrDefault(uid)))
                continue;

            if (weapon.Type == ShipWeaponType.Turret)
            {
                // Manual fire only needs the target inside the arc; auto-fire waits for alignment.
                if (!IsInsideArc(weapon, xform, target.Value))
                    continue;
            }
            else if (!IsFixedTargetAhead(weapon, xform, target.Value) || !IsExposed(xform))
            {
                continue;
            }

            TryFire(uid, weapon, xform, target.Value);
            fired = true;
        }

        return fired;
    }

    private void UpdateGrid(EntityUid grid, ShipCombatComponent combat, float frameTime)
    {
        var groups = BuildGroups(grid);
        var autoFire = combat.AutomaticFire && combat.Condition != ShipCondition.Green;
        var target = autoFire ? GetTargetCoordinates(grid, combat) : null;

        var query = EntityQueryEnumerator<ShipWeaponComponent, BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var weapon, out var powered, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            // Damage scales with the Weapons pool; unpowered weapons deal nothing.
            if (TryComp<GunDamageMultiplierComponent>(uid, out var multiplier))
                multiplier.Multiplier = powered.Powered ? powered.Performance : 0f;

            if (!powered.Powered)
                continue;

            var armed = IsArmed(combat.WeaponMode, weapon, groups.GetValueOrDefault(uid));
            var trackTarget = target != null && armed && IsInsideArc(weapon, xform, target.Value);

            if (weapon.Type == ShipWeaponType.Turret)
                UpdateTurret(uid, weapon, xform, trackTarget ? target : null, frameTime);

            if (!armed || target == null)
                continue;

            if (weapon.Type == ShipWeaponType.Turret)
            {
                if (!trackTarget || !IsAligned(weapon, xform, target.Value))
                    continue;
            }
            else if (!IsFixedTargetAhead(weapon, xform, target.Value) || !IsExposed(xform))
            {
                continue;
            }

            TryFire(uid, weapon, xform, target.Value);
        }
    }

    private void TryFire(EntityUid uid, ShipWeaponComponent weapon, TransformComponent xform, EntityCoordinates target)
    {
        if (!TryComp<GunComponent>(uid, out var gun))
            return;

        // Turrets aim at the target; fixed weapons fire straight along their facing.
        var to = weapon.Type == ShipWeaponType.Turret
            ? target
            : xform.Coordinates.Offset(xform.LocalRotation.ToWorldVec() * 100f);

        _gun.AttemptShoot(uid, uid, gun, to);
    }

    private static bool IsArmed(ShipWeaponMode mode, ShipWeaponComponent weapon, ShipWeaponMode? group)
    {
        if (mode == ShipWeaponMode.MasterSafe && !weapon.FiresWhileSafe)
            return false;

        return mode switch
        {
            ShipWeaponMode.All => true,
            ShipWeaponMode.MasterSafe => weapon.FiresWhileSafe,
            _ => group == mode,
        };
    }

    /// <summary>
    /// The weapon groups from the console links on this grid: sink port id to group.
    /// </summary>
    private Dictionary<EntityUid, ShipWeaponMode> BuildGroups(EntityUid grid)
    {
        var groups = new Dictionary<EntityUid, ShipWeaponMode>();

        var query = EntityQueryEnumerator<DeviceLinkSourceComponent, TransformComponent>();
        while (query.MoveNext(out _, out var source, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            foreach (var (sink, ports) in source.LinkedPorts)
            {
                foreach (var (_, sinkPort) in ports)
                {
                    if (PortModes.TryGetValue(sinkPort.Id, out var mode))
                        groups[sink] = mode;
                }
            }
        }

        return groups;
    }

    /// <summary>
    /// The locked grid's centre as firing coordinates.
    /// </summary>
    private EntityCoordinates? GetTargetCoordinates(EntityUid grid, ShipCombatComponent combat)
    {
        if (combat.LockTarget is not { } target || !Exists(target) || !HasComp<MapGridComponent>(target))
            return null;

        var center = TryComp<PhysicsComponent>(target, out var physics)
            ? physics.LocalCenter
            : Vector2.Zero;

        return new EntityCoordinates(target, center);
    }

    private void UpdateTurret(EntityUid uid, ShipWeaponComponent weapon, TransformComponent xform, EntityCoordinates? target, float frameTime)
    {
        var goal = target != null && GetAngleToTarget(xform, target.Value) is { } angle
            ? angle
            : GetIdleGoal(weapon);

        var current = xform.LocalRotation;
        var delta = WrapAngle(goal.Theta - current.Theta);
        var maxStep = Angle.FromDegrees(weapon.RotationSpeed * frameTime).Theta;
        var step = Math.Clamp(delta, -maxStep, maxStep);

        // Flip the idle sweep when a bound is reached.
        if (target == null && Math.Abs(delta) < 0.01)
            weapon.IdleClockwise = !weapon.IdleClockwise;

        if (Math.Abs(step) < 0.0001)
            return;

        _transform.SetLocalRotation(uid, current + new Angle(step), xform);
    }

    private static Angle GetIdleGoal(ShipWeaponComponent weapon)
    {
        var half = Angle.FromDegrees(weapon.ArcHalfAngle);
        return weapon.IdleClockwise ? weapon.BaseAngle + half : weapon.BaseAngle - half;
    }

    private bool IsInsideArc(ShipWeaponComponent weapon, TransformComponent xform, EntityCoordinates target)
    {
        if (GetAngleToTarget(xform, target) is not { } angle)
            return false;

        return Math.Abs(WrapAngle(angle.Theta - weapon.BaseAngle.Theta))
               <= Angle.FromDegrees(weapon.ArcHalfAngle).Theta;
    }

    private bool IsAligned(ShipWeaponComponent weapon, TransformComponent xform, EntityCoordinates target)
    {
        if (GetAngleToTarget(xform, target) is not { } angle)
            return false;

        return Math.Abs(WrapAngle(angle.Theta - xform.LocalRotation.Theta))
               <= Angle.FromDegrees(weapon.FireTolerance).Theta;
    }

    private bool IsFixedTargetAhead(ShipWeaponComponent weapon, TransformComponent xform, EntityCoordinates target)
    {
        if (GetAngleToTarget(xform, target) is not { } angle)
            return false;

        return Math.Abs(WrapAngle(angle.Theta - xform.LocalRotation.Theta))
               <= Angle.FromDegrees(weapon.FixedFireAngle).Theta;
    }

    /// <summary>
    /// The angle to a target in the weapon's local frame.
    /// </summary>
    private Angle? GetAngleToTarget(TransformComponent xform, EntityCoordinates target)
    {
        var from = _transform.GetMapCoordinates(xform).Position;
        var to = _transform.ToMapCoordinates(target).Position;
        var direction = to - from;
        if (direction.LengthSquared() < 0.01f)
            return null;

        var gridRotation = xform.GridUid is { } grid ? _transform.GetWorldRotation(grid) : Angle.Zero;
        return direction.ToWorldAngle() - gridRotation;
    }

    /// <summary>
    /// Fixed weapons need their muzzle exposed to space (like a thruster nozzle).
    /// </summary>
    private bool IsExposed(TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return true;

        var (x, y) = xform.LocalPosition + xform.LocalRotation.ToWorldVec();
        var tile = _mapSystem.GetTileRef(gridUid, grid, new Vector2i((int) MathF.Floor(x), (int) MathF.Floor(y)));
        return _turf.IsSpace(tile);
    }

    private static double WrapAngle(double theta)
    {
        return Math.IEEERemainder(theta, Math.Tau);
    }
}
