using System.Numerics;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Trigger;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: launches, steers and tracks ship torpedoes, and runs the incoming-torpedo alarms.
/// </summary>
/// <remarks>
/// Torpedoes are ordinary projectiles (so the bluespace dock's projectile cleanup applies) that are
/// steered towards the grid captured at launch with a limited turn rate: a manoeuvring target can
/// make them miss. Launchers are fired manually from the tactical console (never automatically),
/// need a set lock, power and exposed space in front, and each has its own cooldown.
/// </remarks>
public sealed class ShipTorpedoSystem : EntitySystem
{
    [Dependency] private readonly ShipCombatSystem _combat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedProjectileSystem _projectiles = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ShipShieldSystem _shields = default!;

    /// <summary>Delay between the torpedo contacting the target and detonating.</summary>
    private static readonly TimeSpan ContactFuse = TimeSpan.FromSeconds(0.1);

    /// <summary>How often the incoming-torpedo scan runs.</summary>
    private static readonly TimeSpan AlarmScanInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Distance under which the alarm switches to the fast close beep.</summary>
    private const float CloseAlarmRange = 64f;

    private TimeSpan _nextAlarmScan;

    /// <summary>Stagger between two launchers of a ripple volley, in seconds.</summary>
    private const float RippleDelay = 0.25f;

    /// <summary>Queued ripple volleys per ship (runtime).</summary>
    private readonly Dictionary<EntityUid, List<(EntityUid Launcher, TimeSpan At)>> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipTorpedoComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(EntityUid uid, ShipTorpedoComponent torpedo, ref ProjectileHitEvent args)
    {
        if (torpedo.Detonating)
            return;

        // A direct hit detonates on impact.
        Detonate(uid, torpedo);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        ProcessRipples();
        DetonateOnContact();
        DetonateDue();
        StartContactFuses();
        Steer(frameTime);

        if (_timing.CurTime < _nextAlarmScan)
            return;

        _nextAlarmScan = _timing.CurTime + AlarmScanInterval;
        UpdateAlarms();
    }

    /// <summary>
    /// Fires one torpedo from every ready launcher on the ship (the console's FIRE TORPEDOES button).
    /// Requires a set lock, an armed weapon mode and a non-GREEN condition; each launcher respects
    /// its own cooldown and must have exposed space in front.
    /// </summary>
    /// <summary>
    /// Fires a staggered volley (ripple) from every ready launcher on the ship. Requires a set lock,
    /// an armed weapon mode and a non-GREEN condition; each launcher respects its own cooldown and
    /// must have exposed space in front.
    /// </summary>
    public bool TryLaunch(EntityUid console)
    {
        if (!_combat.TryGetShipCombat(console, out var combat))
            return false;

        var grid = combat.Owner;
        if (combat.Comp.Condition == ShipCondition.Green
            || combat.Comp.WeaponMode == ShipWeaponMode.MasterSafe
            || combat.Comp.LockInProgress)
        {
            return false;
        }

        if (combat.Comp.LockTarget is not { } target
            || !Exists(target)
            || !HasComp<MapGridComponent>(target))
        {
            return false;
        }

        // Already rippling: one press per volley.
        if (_pending.ContainsKey(grid))
            return false;

        var queue = new List<(EntityUid Launcher, TimeSpan At)>();
        var query = EntityQueryEnumerator<ShipTorpedoLauncherComponent, BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var launcher, out var powered, out var xform))
        {
            if (xform.GridUid != grid
                || !powered.Powered
                || _timing.CurTime < launcher.NextFire
                || !IsExposed(xform))
            {
                continue;
            }

            queue.Add((uid, _timing.CurTime + TimeSpan.FromSeconds(queue.Count * RippleDelay)));
        }

        if (queue.Count == 0)
            return false;

        _pending[grid] = queue;
        return true;
    }

    /// <summary>
    /// Fires queued ripple launches when their staggered times come up.
    /// </summary>
    private void ProcessRipples()
    {
        if (_pending.Count == 0)
            return;

        List<EntityUid>? finished = null;
        foreach (var (grid, queue) in _pending)
        {
            while (queue.Count > 0 && _timing.CurTime >= queue[0].At)
            {
                var (launcherUid, _) = queue[0];
                queue.RemoveAt(0);

                if (TryComp<ShipTorpedoLauncherComponent>(launcherUid, out var launcher)
                    && TryComp<TransformComponent>(launcherUid, out var xform)
                    && TryComp<ShipCombatComponent>(grid, out var combat)
                    && combat.LockTarget is { } target
                    && Exists(target))
                {
                    LaunchOne(launcherUid, launcher, xform, target, combat);
                }
            }

            if (queue.Count == 0)
                (finished ??= new List<EntityUid>()).Add(grid);
        }

        if (finished == null)
            return;

        foreach (var grid in finished)
            _pending.Remove(grid);
    }

    /// <summary>
    /// Spawns one torpedo from a launcher, aimed like the weapons are.
    /// </summary>
    private void LaunchOne(EntityUid uid, ShipTorpedoLauncherComponent launcher, TransformComponent xform,
        EntityUid target, ShipCombatComponent combat)
    {
        if (xform.MapUid is not { } mapUid || _timing.CurTime < launcher.NextFire || !IsExposed(xform))
            return;

        launcher.NextFire = _timing.CurTime + launcher.FireCooldown;

        // Launch from the exposed muzzle, in front of the launcher.
        var from = _transform.GetMapCoordinates(xform).Position + xform.LocalRotation.ToWorldVec() * 1.5f;
        var torpedo = Spawn(launcher.Torpedo.Id, new MapCoordinates(from, xform.MapID));

        if (!TryComp<ShipTorpedoComponent>(torpedo, out var torpedoComp))
            return;

        // The launcher is the shooter: torpedoes ignore it on the way out and a misfire at our
        // own ship counts as our own weapon (bypassing our own shields).
        if (TryComp<ProjectileComponent>(torpedo, out var projectile))
            _projectiles.SetShooter(torpedo, projectile, uid);

        torpedoComp.Target = target;
        torpedoComp.Source = combat.Owner;
        torpedoComp.LaunchTime = _timing.CurTime;

        // Torpedoes aim at the same thing the weapons were aiming at; CREW falls back to the hull.
        if (combat.TargetMode != ShipTargetMode.Crew && combat.AimTarget is { } aimEntity && Exists(aimEntity))
            torpedoComp.TargetEntity = aimEntity;

        var aim = GridCenter(target);
        var direction = aim - from;
        if (direction.LengthSquared() > 0.01f)
        {
            var heading = direction.ToWorldAngle();
            if (TryComp<PhysicsComponent>(torpedo, out var physics))
                _physics.SetLinearVelocity(torpedo, heading.ToWorldVec() * torpedoComp.Speed, body: physics);

            _transform.SetWorldRotationNoLerp(torpedo, heading);
        }

        if (launcher.LaunchSound != null)
            _audio.PlayPvs(launcher.LaunchSound, torpedo);
    }

    /// <summary>
    /// Detonates a torpedo that swept into a wall, window or machine since the last tick. This is
    /// independent of the projectile hit event, so a torpedo can never pass through a wall.
    /// </summary>
    private void DetonateOnContact()
    {
        var query = EntityQueryEnumerator<ShipTorpedoComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var torpedo, out var xform))
        {
            if (torpedo.Detonating)
                continue;

            var position = _transform.GetWorldPosition(xform);
            if (torpedo.LastPosition is { } last)
            {
                var delta = position - last;
                if (delta.LengthSquared() > 0.0001f)
                {
                    var ray = new CollisionRay(last, delta.Normalized(),
                        (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable));
                    foreach (var hit in _physics.IntersectRay(xform.MapID, ray, delta.Length(), uid, true))
                    {
                        // The launching ship's own hull never counts: a torpedo that has to turn
                        // away from the tube must be able to leave its own ship first.
                        if (hit.HitEntity.IsValid()
                            && (Transform(hit.HitEntity).GridUid == torpedo.Source
                                || HasComp<ShipTorpedoComponent>(hit.HitEntity)))
                        {
                            continue;
                        }

                        Detonate(uid, torpedo);
                        break;
                    }
                }
            }

            torpedo.LastPosition = position;
        }
    }

    /// <summary>
    /// Detonates a torpedo: a ship with its shields up absorbs the whole torpedo, otherwise it
    /// explodes with its full blast.
    /// </summary>
    private void Detonate(EntityUid uid, ShipTorpedoComponent torpedo)
    {
        if (Deleted(uid) || torpedo.Detonated)
            return;

        torpedo.Detonated = true;
        torpedo.Detonating = true;

        if (torpedo.Target is { } target
            && TryComp<ShipShieldComponent>(target, out var shield)
            && _shields.TryAbsorb(target, shield, _transform.GetWorldPosition(uid), torpedo.ShieldDamage))
        {
            QueueDel(uid);
            return;
        }

        var trigger = new TriggerEvent();
        RaiseLocalEvent(uid, ref trigger);
        QueueDel(uid);
    }

    /// <summary>
    /// Detonates torpedoes whose contact fuse has run out.
    /// </summary>
    private void DetonateDue()
    {
        var query = EntityQueryEnumerator<ShipTorpedoComponent>();
        while (query.MoveNext(out var uid, out var torpedo))
        {
            if (!torpedo.Detonating || _timing.CurTime < torpedo.DetonateAt)
                continue;

            Detonate(uid, torpedo);
        }
    }

    /// <summary>
    /// A torpedo flying over the target's tiles without hitting anything starts its 0.75 s fuse, so
    /// it detonates inside the hull.
    /// </summary>
    private void StartContactFuses()
    {
        var query = EntityQueryEnumerator<ShipTorpedoComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var torpedo, out var xform))
        {
            if (torpedo.Detonating || torpedo.Target is not { } target || !Exists(target))
                continue;

            if (!IsOverGrid(xform, target))
                continue;

            torpedo.Detonating = true;
            torpedo.DetonateAt = _timing.CurTime + ContactFuse;

            if (TryComp<PhysicsComponent>(uid, out var physics))
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
        }
    }

    /// <summary>
    /// True when the entity's position is inside the grid's bounds.
    /// </summary>
    private bool IsOverGrid(TransformComponent xform, EntityUid grid)
    {
        var world = new MapCoordinates(_transform.GetWorldPosition(xform), xform.MapID);
        return _mapManager.TryFindGridAt(world, out var foundGrid, out _) && foundGrid == grid;
    }

    /// <summary>
    /// Turns every torpedo towards its target grid, limited by its turn rate.
    /// </summary>
    private void Steer(float frameTime)
    {
        var query = EntityQueryEnumerator<ShipTorpedoComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var torpedo, out var physics, out var xform))
        {
            if (torpedo.Target is not { } target || !Exists(target) || !HasComp<MapGridComponent>(target))
                continue;

            var age = _timing.CurTime - torpedo.LaunchTime;
            if (age < torpedo.SteeringDelay)
                continue;

            var ramp = torpedo.SteeringRamp <= TimeSpan.Zero
                ? 1f
                : Math.Clamp((float) ((age - torpedo.SteeringDelay) / torpedo.SteeringRamp), 0f, 1f);

            var position = _transform.GetWorldPosition(xform);
            var aim = torpedo.TargetEntity is { } aimEntity && Exists(aimEntity)
                ? _transform.GetWorldPosition(aimEntity)
                : GridCenter(target);
            var toTarget = aim - position;
            if (toTarget.LengthSquared() < 0.01f)
                continue;

            var current = physics.LinearVelocity.LengthSquared() < 0.01f
                ? toTarget.ToWorldAngle()
                : physics.LinearVelocity.ToWorldAngle();
            var desired = toTarget.ToWorldAngle();

            var maxTurn = Angle.FromDegrees(torpedo.TurnRate * ramp * frameTime).Theta;
            var turn = Math.Clamp(WrapAngle(desired.Theta - current.Theta), -maxTurn, maxTurn);
            var heading = current + new Angle(turn);

            _physics.SetLinearVelocity(uid, heading.ToWorldVec() * torpedo.Speed, body: physics);
            _transform.SetWorldRotationNoLerp(uid, heading);
        }
    }

    /// <summary>
    /// Finds inbound torpedoes per ship and runs the alarm: alert14 once on first detection, then
    /// alert19 (800-200 tiles) or the fast alert18 beep (under 200) until it hits or misses.
    /// </summary>
    private void UpdateAlarms()
    {
        var inbound = new Dictionary<EntityUid, float>();

        var torpedoes = EntityQueryEnumerator<ShipTorpedoComponent, PhysicsComponent, TransformComponent>();
        while (torpedoes.MoveNext(out _, out var torpedo, out var physics, out var xform))
        {
            if (torpedo.Target is not { } target || !Exists(target))
                continue;

            var toTarget = GridCenter(target) - _transform.GetWorldPosition(xform);
            var distance = toTarget.Length();
            if (distance < 0.1f || distance > torpedo.AlarmRange)
                continue;

            // Only count torpedoes actually heading at the ship: a miss is silent.
            var velocity = physics.LinearVelocity;
            if (velocity.LengthSquared() > 0.01f
                && Math.Abs(WrapAngle(velocity.ToWorldAngle().Theta - toTarget.ToWorldAngle().Theta))
                   > Angle.FromDegrees(torpedo.InboundDeviation).Theta)
            {
                continue;
            }

            if (!inbound.TryGetValue(target, out var closest) || distance < closest)
                inbound[target] = distance;
        }

        var ships = EntityQueryEnumerator<ShipCombatComponent>();
        while (ships.MoveNext(out var grid, out var combat))
        {
            var wasIncoming = combat.IncomingMissile;
            combat.IncomingMissile = inbound.TryGetValue(grid, out var nearest);

            if (!combat.IncomingMissile)
            {
                combat.MissileWarningPlayed = false;
                continue;
            }

            // First detection: the "incoming torpedo" alert once.
            if (!wasIncoming || !combat.MissileWarningPlayed)
            {
                combat.MissileWarningPlayed = true;
                PlayMissileSound(grid, MissileAlarm.Detected);
            }

            if (_timing.CurTime < combat.NextMissileBeep)
                continue;

            combat.NextMissileBeep = _timing.CurTime
                                     + (nearest < CloseAlarmRange ? TimeSpan.FromSeconds(0.35) : TimeSpan.FromSeconds(1));

            PlayMissileSound(grid, nearest < CloseAlarmRange ? MissileAlarm.Close : MissileAlarm.Warning);
        }
    }

    private enum MissileAlarm : byte
    {
        Detected,
        Warning,
        Close,
    }

    private void PlayMissileSound(EntityUid grid, MissileAlarm alarm)
    {
        var query = EntityQueryEnumerator<ShipTargetingConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.GridUid != grid || console.Muted)
                continue;

            var sound = alarm switch
            {
                MissileAlarm.Detected => console.MissileDetectedSound,
                MissileAlarm.Warning => console.MissileWarningSound,
                _ => console.MissileCloseSound,
            };

            _audio.PlayPvs(sound, uid);
        }
    }

    /// <summary>
    /// Plain torpedo launch needs the muzzle tile in front of the launcher to be space.
    /// </summary>
    private bool IsExposed(TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return true;

        var (x, y) = xform.LocalPosition + xform.LocalRotation.ToWorldVec();
        var tile = _mapSystem.GetTileRef(gridUid, grid, new Vector2i((int) MathF.Floor(x), (int) MathF.Floor(y)));
        return _turf.IsSpace(tile);
    }

    private Vector2 GridCenter(EntityUid grid)
    {
        if (TryComp<PhysicsComponent>(grid, out var physics))
            return _transform.ToMapCoordinates(new EntityCoordinates(grid, physics.LocalCenter)).Position;

        return _transform.GetMapCoordinates(grid).Position;
    }

    private static double WrapAngle(double theta)
    {
        return Math.IEEERemainder(theta, Math.Tau);
    }
}
