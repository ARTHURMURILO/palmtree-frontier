using System.Numerics;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: grid-level lock management and damage reactions (automatic condition and retaliation).
/// </summary>
/// <remarks>
/// The tactical console is only one possible driver: locks are started through this system's API and
/// state changes are broadcast, so a future AI brain can lock and fight without a console. Locks are
/// shared per grid and only reference the target grid at runtime - they are never saved.
/// </remarks>
public sealed class ShipTargetingSystem : EntitySystem
{
    [Dependency] private readonly ShipCombatSystem _combat = default!;
    [Dependency] private readonly ShipShieldSystem _shields = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>Lock time used when the ship has no tactical console (AI ships).</summary>
    private static readonly TimeSpan DefaultLockTime = TimeSpan.FromSeconds(2);

    private enum ConsoleSound
    {
        LockStart,
        LockDenied,
        LockCleared,
        IncomingLock,
    }

    public override void Initialize()
    {
        base.Initialize();

        // BeforeDamageChangedEvent fires before resistances: being shot at raises the alert even
        // when the ship's armour absorbs the whole hit.
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<ShipCombatConditionChangedEvent>(OnConditionChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Collect first: completing a lock can add a component to the target grid, which would
        // invalidate the query while it is still being enumerated.
        List<EntityUid>? toComplete = null;
        List<EntityUid>? toClear = null;

        var query = EntityQueryEnumerator<ShipCombatComponent>();
        while (query.MoveNext(out var grid, out var combat))
        {
            if (combat.LockInProgress && _timing.CurTime >= combat.LockCompleteTime)
            {
                toComplete ??= new List<EntityUid>();
                toComplete.Add(grid);
                continue;
            }

            if (combat.LockTarget is { } target
                && (!Exists(target)
                    || !HasComp<MapGridComponent>(target)
                    || (combat.LockRange > 0f && !InRange(grid, target, combat.LockRange))))
            {
                toClear ??= new List<EntityUid>();
                toClear.Add(grid);
            }
        }

        if (toComplete != null)
        {
            foreach (var grid in toComplete)
            {
                if (TryComp<ShipCombatComponent>(grid, out var combat))
                    CompleteLock(grid, combat);
            }
        }

        if (toClear != null)
        {
            foreach (var grid in toClear)
            {
                if (TryComp<ShipCombatComponent>(grid, out var combat))
                    ClearLock(grid, combat, silent: true);
            }
        }

        // Drop incoming locks from grids that no longer exist.
        var pruneQuery = EntityQueryEnumerator<ShipCombatComponent>();
        while (pruneQuery.MoveNext(out var grid, out var combat))
        {
            if (PruneIncomingLocks(combat))
                RaiseStateChanged(grid);
        }
    }

    /// <summary>
    /// Starts acquiring a lock on <paramref name="targetGrid"/> for the ship <paramref name="source"/>
    /// is on. Returns false when the lock is refused (a deny sound plays at a console source).
    /// </summary>
    /// <param name="range">Maximum distance to the target; 0 means unlimited.</param>
    public bool TryStartLock(EntityUid source, EntityUid targetGrid, float range)
    {
        if (!_combat.TryGetShipCombat(source, out var combat))
            return false;

        // While a lock is being acquired the fire control is busy: spam-clicking cannot retarget
        // and does not beep.
        if (combat.Comp.LockInProgress)
            return false;

        if (!HasComp<MapGridComponent>(targetGrid)
            || targetGrid == combat.Owner
            || combat.Comp.Condition == ShipCondition.Green
            || combat.Comp.WeaponMode == ShipWeaponMode.MasterSafe
            || (range > 0f && !InRange(combat.Owner, targetGrid, range)))
        {
            PlaySound(combat.Owner, ConsoleSound.LockDenied, source);
            return false;
        }

        if (combat.Comp.LockTarget == targetGrid && !combat.Comp.LockInProgress)
            return true;

        // Moving the lock: the old target is no longer being locked by this ship.
        if (combat.Comp.LockTarget is { } previous && previous != targetGrid)
            RemoveIncomingLock(previous, combat.Owner);

        combat.Comp.LockTarget = targetGrid;
        combat.Comp.LockInProgress = true;
        combat.Comp.LockRange = range;
        combat.Comp.LockCompleteTime = _timing.CurTime + GetLockTime(combat.Owner);

        PlaySound(combat.Owner, ConsoleSound.LockStart, source);
        RaiseStateChanged(combat.Owner);
        return true;
    }

    /// <summary>
    /// Clears the lock of the ship <paramref name="source"/> is on. Returns true when a lock was
    /// actually cleared.
    /// </summary>
    public bool TryClearLock(EntityUid source)
    {
        if (!_combat.TryGetShipCombat(source, out var combat))
            return false;

        return ClearLock(combat.Owner, combat.Comp, silent: false, source);
    }

    private bool ClearLock(EntityUid grid, ShipCombatComponent combat, bool silent, EntityUid? source = null)
    {
        var hadLock = combat.LockTarget != null || combat.LockInProgress;

        if (combat.LockTarget is { } target && Exists(target))
            RemoveIncomingLock(target, grid);

        combat.LockTarget = null;
        combat.LockInProgress = false;
        combat.LockRange = 0f;

        if (!hadLock)
            return false;

        RaiseStateChanged(grid);

        if (!silent)
            PlaySound(grid, ConsoleSound.LockCleared, source);

        return true;
    }

    private void CompleteLock(EntityUid grid, ShipCombatComponent combat)
    {
        combat.LockInProgress = false;

        if (combat.LockTarget is not { } target || !Exists(target) || !HasComp<MapGridComponent>(target))
        {
            ClearLock(grid, combat, silent: true);
            return;
        }

        // The target may not have combat state yet (an asteroid, a derelict, ...).
        var targetCombat = EnsureComp<ShipCombatComponent>(target);
        var added = targetCombat.IncomingLocks.Add(grid);

        RaiseStateChanged(grid);
        RaiseStateChanged(target);

        if (added)
            PlaySound(target, ConsoleSound.IncomingLock);
    }

    private void OnConditionChanged(ShipCombatConditionChangedEvent args)
    {
        // Dropping to peacetime releases the lock.
        if (args.NewCondition != ShipCondition.Green)
            return;

        if (TryComp<ShipCombatComponent>(args.Grid, out var combat))
            ClearLock(args.Grid, combat, silent: true);
    }

    private void OnBeforeDamageChanged(EntityUid uid, DamageableComponent damageable, ref BeforeDamageChangedEvent args)
    {
        if (args.Damage.GetTotal() <= FixedPoint2.Zero)
            return;

        // Ship shields absorb the hit first; an absorbed hit still counts as being attacked, so the
        // reactions below run either way.
        _shields.TryAbsorb(uid, ref args);

        if (!_combat.TryGetShipCombat(uid, out var combat))
            return;

        // Automatic condition: any damage to the ship raises GREEN to YELLOW.
        if (combat.Comp.AutomaticCondition && combat.Comp.Condition == ShipCondition.Green)
            _combat.SetCondition(combat, ShipCondition.Yellow);

        // Automatic retaliation: only ship weapons (a grid-anchored shooter, not a person with a gun).
        if (!combat.Comp.AutomaticRetaliation || combat.Comp.LockTarget != null || combat.Comp.LockInProgress)
            return;

        if (args.Origin is not { } origin || HasComp<MobStateComponent>(origin))
            return;

        if (Transform(origin).GridUid is not { } attackerGrid || attackerGrid == combat.Owner)
            return;

        // Being attacked is enough to escalate out of peacetime.
        if (combat.Comp.Condition == ShipCondition.Green)
            _combat.SetCondition(combat, ShipCondition.Yellow);

        // Pull the ship out of MASTER SAFE, then lock the attacker.
        if (combat.Comp.WeaponMode == ShipWeaponMode.MasterSafe)
        {
            combat.Comp.WeaponMode = ShipWeaponMode.All;
            RaiseStateChanged(combat.Owner);
        }

        TryStartLock(combat.Owner, attackerGrid, GetMaxConsoleRange(combat.Owner));
    }

    private bool PruneIncomingLocks(ShipCombatComponent combat)
    {
        if (combat.IncomingLocks.Count == 0)
            return false;

        List<EntityUid>? stale = null;
        foreach (var locker in combat.IncomingLocks)
        {
            if (Exists(locker) && HasComp<MapGridComponent>(locker))
                continue;

            stale ??= new List<EntityUid>();
            stale.Add(locker);
        }

        if (stale == null)
            return false;

        foreach (var locker in stale)
            combat.IncomingLocks.Remove(locker);

        return true;
    }

    private void RemoveIncomingLock(EntityUid target, EntityUid locker)
    {
        if (!TryComp<ShipCombatComponent>(target, out var combat))
            return;

        if (combat.IncomingLocks.Remove(locker))
            RaiseStateChanged(target);
    }

    private void RaiseStateChanged(EntityUid grid)
    {
        RaiseLocalEvent(new ShipCombatStateChangedEvent(grid));
    }

    /// <summary>
    /// The lock time of a ship: the fastest tactical console on it, or the default.
    /// </summary>
    private TimeSpan GetLockTime(EntityUid grid)
    {
        var lockTime = DefaultLockTime;

        var query = EntityQueryEnumerator<ShipTargetingConsoleComponent, TransformComponent>();
        while (query.MoveNext(out _, out var console, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            if (console.LockTime < lockTime)
                lockTime = console.LockTime;
        }

        return lockTime;
    }

    /// <summary>
    /// The longest radar range among a ship's tactical consoles, or 0 (unlimited) when it has none.
    /// </summary>
    private float GetMaxConsoleRange(EntityUid grid)
    {
        var range = 0f;

        var query = EntityQueryEnumerator<ShipTargetingConsoleComponent, RadarConsoleComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var radar, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            range = MathF.Max(range, radar.MaxRange);
        }

        return range;
    }

    /// <summary>
    /// Plays a console sound: on the acting console for user actions, or on every console of the
    /// ship for automatic ones. Sounds are audible around the console whether or not its window is open.
    /// </summary>
    private void PlaySound(EntityUid grid, ConsoleSound sound, EntityUid? source = null)
    {
        if (source != null && TryComp<ShipTargetingConsoleComponent>(source, out var sourceConsole))
        {
            _audio.PlayPvs(GetSound(sourceConsole, sound), source.Value);
            return;
        }

        var query = EntityQueryEnumerator<ShipTargetingConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            _audio.PlayPvs(GetSound(console, sound), uid);
        }
    }

    private static SoundSpecifier GetSound(ShipTargetingConsoleComponent console, ConsoleSound sound)
    {
        return sound switch
        {
            ConsoleSound.LockStart => console.LockStartSound,
            ConsoleSound.LockDenied => console.LockDeniedSound,
            ConsoleSound.LockCleared => console.LockClearedSound,
            ConsoleSound.IncomingLock => console.IncomingLockSound,
            _ => console.LockStartSound,
        };
    }

    private bool InRange(EntityUid a, EntityUid b, float range)
    {
        return (GridCenter(a) - GridCenter(b)).Length() <= range;
    }

    private Vector2 GridCenter(EntityUid grid)
    {
        if (TryComp<PhysicsComponent>(grid, out var physics))
            return _transform.ToMapCoordinates(new EntityCoordinates(grid, physics.LocalCenter)).Position;

        return _transform.GetMapCoordinates(grid).Position;
    }
}
