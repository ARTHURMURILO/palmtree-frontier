using System.Numerics;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Maps;
using Content.Shared.Projectiles;
using Content.Shared.Trigger;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
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
    [Dependency] private readonly SharedProjectileSystem _projectiles = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ShipShieldSystem _shields = default!;

    /// <summary>Delay between the torpedo contacting the target and detonating.</summary>
    private static readonly TimeSpan ContactFuse = TimeSpan.FromSeconds(0.75);

    /// <summary>How often the incoming-torpedo scan runs.</summary>
    private static readonly TimeSpan AlarmScanInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Distance under which the alarm switches to the fast close beep.</summary>
    private const float CloseAlarmRange = 64f;

    private TimeSpan _nextAlarmScan;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipTorpedoComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(EntityUid uid, ShipTorpedoComponent torpedo, ref ProjectileHitEvent args)
    {
        if (torpedo.Detonating)
            return;

        // Contact: hold position for the fuse (0.75 s), then detonate. If the torpedo penetrated the
        // hull it explodes inside the ship.
        torpedo.Detonating = true;
        torpedo.DetonateAt = _timing.CurTime + ContactFuse;

        if (TryComp<PhysicsComponent>(uid, out var physics))
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        DetonateDue();
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

        var fired = false;
        var query = EntityQueryEnumerator<ShipTorpedoLauncherComponent, BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var launcher, out var powered, out var xform))
        {
            if (xform.GridUid != grid
                || xform.MapUid is not { } mapUid
                || !powered.Powered
                || _timing.CurTime < launcher.NextFire
                || !IsExposed(xform))
            {
                continue;
            }

            launcher.NextFire = _timing.CurTime + launcher.FireCooldown;

            // Launch from the exposed muzzle, in front of the launcher.
            var from = _transform.GetMapCoordinates(xform).Position + xform.LocalRotation.ToWorldVec() * 1.5f;
            var torpedo = Spawn(launcher.Torpedo.Id, new MapCoordinates(from, xform.MapID));

            if (!TryComp<ShipTorpedoComponent>(torpedo, out var torpedoComp))
                continue;

            // The launcher is the shooter: torpedoes ignore it on the way out and a misfire at our
            // own ship counts as our own weapon (bypassing our own shields).
            if (TryComp<ProjectileComponent>(torpedo, out var projectile))
                _projectiles.SetShooter(torpedo, projectile, uid);

            torpedoComp.Target = target;
            torpedoComp.Source = grid;

            // Torpedoes aim at the same thing the weapons were aiming at; CREW falls back to the hull.
            if (combat.Comp.TargetMode != ShipTargetMode.Crew && combat.Comp.AimTarget is { } aimEntity && Exists(aimEntity))
                torpedoComp.TargetEntity = aimEntity;
            torpedoComp.LaunchTime = _timing.CurTime;

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

            fired = true;
        }

        return fired;
    }

    /// <summary>
    /// Detonates torpedoes whose contact fuse has run out. Shields absorb the whole torpedo; without
    /// shields it explodes with its full blast.
    /// </summary>
    private void DetonateDue()
    {
        var query = EntityQueryEnumerator<ShipTorpedoComponent>();
        while (query.MoveNext(out var uid, out var torpedo))
        {
            if (!torpedo.Detonating || _timing.CurTime < torpedo.DetonateAt)
                continue;

            if (torpedo.Target is { } target
                && TryComp<ShipShieldComponent>(target, out var shield)
                && _shields.TryAbsorb(target, shield, _transform.GetWorldPosition(uid), torpedo.ShieldDamage))
            {
                // The deflector ate it: no hull damage at all.
                QueueDel(uid);
                continue;
            }

            // ExplodeOnTrigger on the prototype runs the blast.
            var trigger = new TriggerEvent();
            RaiseLocalEvent(uid, ref trigger);
            QueueDel(uid);
        }
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
