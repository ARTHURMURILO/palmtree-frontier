using System.Numerics;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared._PS.Weapons;
using Content.Shared.DeviceLinking;
using Content.Shared.Maps;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Server.Power.Components;
using Content.Shared._PS.CCVar;
using Content.Shared.Damage;
using Content.Shared.Physics;
using Content.Shared.Mobs.Components;
using Content.Server.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
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
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// ps.ship_combat.turrets_shoot_through_hull: when false, turrets hold fire while their own hull
    /// blocks the firing line.
    /// </summary>
    private bool _shootThroughHull;

    /// <summary>How often the weapons re-pick which subsystem they aim at.</summary>
    private static readonly TimeSpan AimReselectInterval = TimeSpan.FromSeconds(2);


    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("shipcombat");

        SubscribeLocalEvent<ShipWeaponComponent, MapInitEvent>(OnMapInit);
        _cfg.OnValueChanged(PSCVars.ShipCombatTurretsShootThroughHull, OnShootThroughHullChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _cfg.UnsubValueChanged(PSCVars.ShipCombatTurretsShootThroughHull, OnShootThroughHullChanged);
    }

    private void OnShootThroughHullChanged(bool value)
    {
        _shootThroughHull = value;
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
        RefreshAimTarget(combat.Comp);
        var target = GetAimCoordinates(combat.Comp);
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

            weapon.ShootThroughHull = weapon.IgnoreOwnGrid && _shootThroughHull;

            if (weapon.Type == ShipWeaponType.Turret)
            {
                // Manual fire only needs the target inside the arc; auto-fire waits for alignment.
                if (!IsInsideArc(weapon, xform, target.Value))
                    continue;

                // Turrets hold fire while their own hull blocks the shot (the cvar restores
                // shooting through).
                if (!weapon.ShootThroughHull && FindHullBlocker(uid, xform, target.Value) is { } blocker)
                {
                    combat.Comp.WeaponsBlocked = true;
                    LogHoldFire(uid, weapon, blocker);
                    continue;
                }
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
        RefreshAimTarget(combat);

        // The aim point exists whenever there is a lock, even with auto-fire off: it is what the
        // hull-blocked warning is measured against.
        var aim = GetAimCoordinates(combat);
        var autoFire = combat.AutomaticFire && combat.Condition != ShipCondition.Green;
        var target = autoFire ? aim : null;
        var weaponsBlocked = false;

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

            weapon.ShootThroughHull = weapon.IgnoreOwnGrid && _shootThroughHull;

            var armed = IsArmed(combat.WeaponMode, weapon, groups.GetValueOrDefault(uid));

            // An armed turret whose own hull blocks the line holds fire; remember it so the console
            // can explain why nothing is shooting.
            if (armed && aim != null && weapon.Type == ShipWeaponType.Turret && !weapon.ShootThroughHull
                && FindHullBlocker(uid, xform, aim.Value) is { } hullBlocker)
            {
                weaponsBlocked = true;
                LogHoldFire(uid, weapon, hullBlocker);
            }

            var trackTarget = target != null && armed && IsInsideArc(weapon, xform, target.Value);

            if (weapon.Type == ShipWeaponType.Turret)
                UpdateTurret(uid, weapon, xform, trackTarget ? target : null, frameTime);

            if (!armed || target == null)
                continue;

            if (weapon.Type == ShipWeaponType.Turret)
            {
                if (!trackTarget || !IsAligned(weapon, xform, target.Value))
                    continue;

                if (!weapon.ShootThroughHull && FindHullBlocker(uid, xform, target.Value) != null)
                    continue;
            }
            else if (!IsFixedTargetAhead(weapon, xform, target.Value) || !IsExposed(xform))
            {
                continue;
            }

            TryFire(uid, weapon, xform, target.Value);
        }

        combat.WeaponsBlocked = weaponsBlocked;
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
    /// Where the weapons should aim: the selected subsystem on the locked grid, or its centre.
    /// </summary>
    private EntityCoordinates? GetAimCoordinates(ShipCombatComponent combat)
    {
        if (combat.LockTarget is not { } target || !Exists(target) || !HasComp<MapGridComponent>(target))
            return null;

        if (combat.AimTarget is { } aim && Exists(aim) && Transform(aim).GridUid == target)
            return Transform(aim).Coordinates;

        var center = TryComp<PhysicsComponent>(target, out var physics)
            ? physics.LocalCenter
            : Vector2.Zero;

        return new EntityCoordinates(target, center);
    }

    /// <summary>
    /// Re-picks the subsystem the weapons aim at (every <see cref="AimReselectInterval"/>, or when
    /// the lock moved or the selected target was destroyed).
    /// </summary>
    private void RefreshAimTarget(ShipCombatComponent combat)
    {
        if (combat.LockTarget is not { } target || !Exists(target))
        {
            combat.AimTarget = null;
            return;
        }

        if (_timing.CurTime < combat.NextAimSelection
            && combat.AimTarget is { } current
            && Exists(current)
            && Transform(current).GridUid == target)
        {
            return;
        }

        combat.AimTarget = GetAimTarget(target, combat.TargetMode);
        combat.NextAimSelection = _timing.CurTime + AimReselectInterval;
    }

    /// <summary>
    /// Picks a random entity of the requested kind on a grid, or null for
    /// <see cref="ShipTargetMode.Random"/> (aim at the grid centre) or when nothing matches.
    /// </summary>
    public EntityUid? GetAimTarget(EntityUid grid, ShipTargetMode mode)
    {
        if (mode == ShipTargetMode.Random)
            return null;

        var matches = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            if (MatchesAimMode(uid, mode))
                matches.Add(uid);
        }

        return matches.Count == 0 ? null : _random.Pick(matches);
    }

    private bool MatchesAimMode(EntityUid uid, ShipTargetMode mode)
    {
        return mode switch
        {
            // Hull: any anchored structure that is not a subsystem.
            ShipTargetMode.Hull => HasComp<DamageableComponent>(uid)
                                   && !HasComp<MobStateComponent>(uid)
                                   && !HasComp<ShipWeaponComponent>(uid)
                                   && !HasComp<ThrusterComponent>(uid)
                                   && !HasComp<PowerSupplierComponent>(uid),
            ShipTargetMode.Generators => HasComp<PowerSupplierComponent>(uid)
                                         || HasComp<BluespacePowerSinkComponent>(uid),
            ShipTargetMode.Weapons => HasComp<ShipWeaponComponent>(uid),
            ShipTargetMode.Thrusters => HasComp<ThrusterComponent>(uid),
            _ => false,
        };
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
    /// The first entity on the weapon's own grid that its shot would hit, or null when the line is
    /// clear. The ray uses the same <see cref="CollisionGroup.Opaque"/> mask a projectile uses, so
    /// the result is exactly "would my own ship eat this shot" - floors do not block, walls,
    /// windows, machines and a wall on the weapon's own tile do.
    /// </summary>
    private EntityUid? FindHullBlocker(EntityUid uid, TransformComponent xform, EntityCoordinates target)
    {
        if (xform.GridUid is not { } gridUid)
            return null;

        var from = _transform.GetMapCoordinates(xform);
        var to = _transform.ToMapCoordinates(target);
        var direction = to.Position - from.Position;
        var distance = direction.Length();
        if (distance < 0.01f)
            return null;

        var ray = new CollisionRay(from.Position, direction.Normalized(), (int) CollisionGroup.Opaque);
        foreach (var hit in _physics.IntersectRay(from.MapId, ray, distance, uid, false))
        {
            if (hit.HitEntity.IsValid() && Transform(hit.HitEntity).GridUid == gridUid)
                return hit.HitEntity;
        }

        return null;
    }

    /// <summary>
    /// Writes (at most every few seconds per weapon) why a turret is holding fire.
    /// </summary>
    private void LogHoldFire(EntityUid uid, ShipWeaponComponent weapon, EntityUid blocker)
    {
        if (_timing.CurTime < weapon.NextHoldFireLog)
            return;

        weapon.NextHoldFireLog = _timing.CurTime + TimeSpan.FromSeconds(5);
        _sawmill.Info($"{ToPrettyString(uid)} holding fire: {ToPrettyString(blocker)} blocks the line of fire "
                      + "(set ps.ship_combat.turrets_shoot_through_hull to true to shoot through the hull)");
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
