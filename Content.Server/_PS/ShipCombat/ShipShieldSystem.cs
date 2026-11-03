using System.Numerics;
using Content.Shared._PS.CCVar;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Damage;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: the ship's directional shields.
/// </summary>
/// <remarks>
/// Four facings (fore/starboard/aft/port in the grid's local frame). A facing with any charge
/// absorbs the whole hit (STO); a facing at 0 goes down and only re-arms above 20% of its maximum.
/// Every six seconds each facing regenerates <c>RegenPerPulse × generator performance</c>; shield
/// power also gives hardness (each point negates 0.2% of absorbed damage). The arrows redistribute
/// current shield energy instantly, with a cooldown.
/// </remarks>
public sealed class ShipShieldSystem : EntitySystem
{
    /// <summary>STO shield regeneration cycle, in seconds.</summary>
    public const float PulseInterval = 6f;

    /// <summary>STO shield hardness: each shield power point negates this fraction of the damage.</summary>
    public const float HardnessPerPoint = 0.002f;

    /// <summary>Cooldown between instant shield redistributions.</summary>
    public static readonly TimeSpan RedistributionCooldown = TimeSpan.FromSeconds(25);

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ShipCombatSystem _combat = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("shipcombat");

        SubscribeLocalEvent<ShieldGeneratorComponent, MapInitEvent>(OnGeneratorInit);
        SubscribeLocalEvent<ShipShieldComponent, ShieldTileDamageAttemptEvent>(OnShieldTileDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipShieldComponent>();
        while (query.MoveNext(out var grid, out var shield))
        {
            UpdateGrid(grid, shield);
        }
    }

    /// <summary>
    /// Resolves the grid of an entity and ensures it has shield state. Idempotent.
    /// </summary>
    public bool TryEnsureShields(EntityUid entity, out Entity<ShipShieldComponent> shields)
    {
        shields = default;

        if (!_combat.TryResolveGrid(entity, out var grid))
            return false;

        if (!TryComp(grid, out ShipShieldComponent? component))
            component = AddComp<ShipShieldComponent>(grid);

        shields = (grid, component);
        return true;
    }

    /// <summary>
    /// Absorbs a damage attempt with the ship's shields. Returns true when the hit was absorbed (the
    /// caller must cancel the damage); false when the hull takes it.
    /// </summary>
    public bool TryAbsorb(EntityUid target, ref BeforeDamageChangedEvent args)
    {
        if (!_combat.TryGetShipCombat(target, out var combat))
            return false;

        var grid = combat.Owner;
        if (!TryComp<ShipShieldComponent>(grid, out var shield))
            return false;

        var total = args.Damage.GetTotal().Float();
        if (total <= 0f)
            return false;

        // The ship's own weapons bypass its shields (stationary weapons punish bad placement).
        if (args.Origin is { } origin && Exists(origin) && Transform(origin).GridUid == grid)
            return false;

        // Direction from the attacker when known; explosions have no origin and hit the strongest facing.
        Vector2? source = null;
        if (args.Origin is { } attacker && Exists(attacker))
            source = _transform.GetMapCoordinates(attacker).Position;

        if (!TryAbsorb(grid, shield, source, total))
            return false;

        args.Cancelled = true;
        SpawnImpactEffect(target);
        return true;
    }

    /// <summary>
    /// Absorbs damage with one facing. Returns true when the shield took the hit.
    /// </summary>
    public bool TryAbsorb(EntityUid grid, ShipShieldComponent shield, Vector2? sourcePosition, float damage)
    {
        if (damage <= 0f)
            return false;

        var facing = GetHitFacing(grid, shield, sourcePosition);
        if (facing == null)
            return false;

        var index = (int) facing.Value;
        if (!shield.Up[index] || shield.Facings[index] <= 0f)
            return false;

        // STO hardness: shield power negates part of the incoming damage.
        var hardness = GetHardness(grid);
        shield.Facings[index] = MathF.Max(0f, shield.Facings[index] - damage * (1f - hardness));

        if (shield.Facings[index] <= 0f)
        {
            shield.Facings[index] = 0f;
            shield.Up[index] = false;
            _sawmill.Info($"{ToPrettyString(grid)} {facing} shields collapsed");

            if (_timing.CurTime >= shield.NextCollapseSound)
            {
                shield.NextCollapseSound = _timing.CurTime + TimeSpan.FromSeconds(2);
                PlayShieldingAlarm(grid, ShieldAlarm.Collapse);
            }

            // All facings down: the heavy alarm, once per blackout.
            var allDown = true;
            foreach (var up in shield.Up)
            {
                if (up)
                {
                    allDown = false;
                    break;
                }
            }

            if (allDown && !shield.AllDown)
            {
                shield.AllDown = true;
                PlayShieldingAlarm(grid, ShieldAlarm.AllDown);
            }
        }

        return true;
    }

    /// <summary>
    /// Instantly redistributes shield energy: everything into one facing (up to its maximum), or
    /// evenly with <see cref="ShieldFacing.All"/>. Has a cooldown.
    /// </summary>
    public bool TryRedistribute(EntityUid source, ShieldFacing facing)
    {
        if (!_combat.TryGetShipCombat(source, out var combat))
            return false;

        var grid = combat.Owner;
        if (!TryComp<ShipShieldComponent>(grid, out var shield))
            return false;

        if (_timing.CurTime < shield.NextRedistribution)
            return false;

        var total = 0f;
        for (var i = 0; i < 4; i++)
            total += shield.Facings[i];

        if (total <= 0f)
            return false;

        var max = GetMaxPerFacing(grid);
        if (max <= 0f)
            return false;

        if (facing == ShieldFacing.All)
        {
            var share = MathF.Min(total / 4f, max);
            for (var i = 0; i < 4; i++)
                shield.Facings[i] = share;
        }
        else
        {
            var index = (int) facing;

            // Only take what the target facing is missing, spread over the other facings; never
            // overdraw and never throw energy away.
            var needed = MathF.Max(0f, max - shield.Facings[index]);
            var available = 0f;
            for (var i = 0; i < 4; i++)
            {
                if (i != index)
                    available += shield.Facings[i];
            }

            var take = MathF.Min(needed, available);
            if (take > 0f)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (i == index || available <= 0f)
                        continue;

                    shield.Facings[i] = MathF.Max(0f, shield.Facings[i] - take * (shield.Facings[i] / available));
                }

                shield.Facings[index] += take;
            }
        }

        // Redistributing into a downed facing can bring it back above the re-arm threshold.
        for (var i = 0; i < 4; i++)
        {
            if (!shield.Up[i] && shield.Facings[i] > max * shield.ReArmFraction)
                shield.Up[i] = true;
        }

        shield.NextRedistribution = _timing.CurTime + RedistributionCooldown;
        _sawmill.Info($"{ToPrettyString(grid)} shields redistributed ({facing})");
        return true;
    }

    /// <summary>
    /// The shield capacity per facing from the ship's powered generators.
    /// </summary>
    public float GetMaxPerFacing(EntityUid grid)
    {
        var max = 0f;

        var query = EntityQueryEnumerator<ShieldGeneratorComponent, BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out _, out var generator, out var powered, out var xform))
        {
            if (xform.GridUid != grid || !powered.Powered)
                continue;

            max += generator.MaxPerFacing;
        }

        return max;
    }

    private void UpdateGrid(EntityUid grid, ShipShieldComponent shield)
    {
        var max = 0f;
        var regen = 0f;

        var query = EntityQueryEnumerator<ShieldGeneratorComponent, BluespacePoweredComponent, TransformComponent>();
        while (query.MoveNext(out _, out var generator, out var powered, out var xform))
        {
            if (xform.GridUid != grid || !powered.Powered)
                continue;

            max += generator.MaxPerFacing;
            regen += generator.RegenPerPulse * powered.Performance;
        }

        if (max <= 0f)
        {
            if (_combat.TryGetShipCombat(grid, out var noGeneratorCombat))
                noGeneratorCombat.Comp.ShieldRegenPowerDraw = 0f;

            return;
        }

        if (!shield.Initialized)
        {
            shield.Initialized = true;
            for (var i = 0; i < 4; i++)
            {
                shield.Facings[i] = max;
                shield.Up[i] = true;
            }

            shield.NextPulse = _timing.CurTime + TimeSpan.FromSeconds(PulseInterval);
            return;
        }

        if (_timing.CurTime < shield.NextPulse)
            return;

        shield.NextPulse = _timing.CurTime + TimeSpan.FromSeconds(PulseInterval);

        // STO: every pulse adds the regeneration to each facing. The relay is charged 0.6 kW for
        // every hit point restored, kept as a draw until the next pulse.
        var restored = 0f;
        for (var i = 0; i < 4; i++)
        {
            var before = shield.Facings[i];
            var value = MathF.Min(before + regen, max);
            shield.Facings[i] = value;
            restored += value - before;

            if (!shield.Up[i] && value > max * shield.ReArmFraction)
            {
                shield.Up[i] = true;
                shield.AllDown = false;
                _sawmill.Info($"{ToPrettyString(grid)} {(ShieldFacing) i} shields re-armed");
                PlayShieldingAlarm(grid, ShieldAlarm.Restored);
            }
        }

        if (_combat.TryGetShipCombat(grid, out var combat))
        {
            var cost = _cfg.GetCVar(PSCVars.ShipCombatShieldRegenPowerPerHp) * 1000f;
            combat.Comp.ShieldRegenPowerDraw = restored * cost;
        }
    }

    private enum ShieldAlarm : byte
    {
        Collapse,
        AllDown,
        Restored,
    }

    /// <summary>
    /// Plays a shield alarm on every shielding console of the ship (silent consoles stay quiet).
    /// </summary>
    private void PlayShieldingAlarm(EntityUid grid, ShieldAlarm alarm)
    {
        var query = EntityQueryEnumerator<ShipShieldingConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.GridUid != grid || console.Muted)
                continue;

            var sound = alarm switch
            {
                ShieldAlarm.Collapse => console.FacingCollapseSound,
                ShieldAlarm.AllDown => console.AllShieldsDownSound,
                _ => console.ShieldRestoredSound,
            };

            _audio.PlayPvs(sound, uid);
        }
    }

    private void OnShieldTileDamage(EntityUid grid, ShipShieldComponent shield, ref ShieldTileDamageAttemptEvent args)
    {
        if (TryAbsorb(grid, shield, args.Epicenter, args.Intensity))
            args.Cancelled = true;
    }

    private void OnGeneratorInit(EntityUid uid, ShieldGeneratorComponent generator, MapInitEvent args)
    {
        _combat.TryEnsureShipCombat(uid, out _);
        TryEnsureShields(uid, out _);
    }

    private ShieldFacing? GetHitFacing(EntityUid grid, ShipShieldComponent shield, Vector2? sourcePosition)
    {
        if (sourcePosition is { } source)
        {
            var direction = source - GridCenter(grid);
            if (direction.LengthSquared() > 0.01f)
            {
                // Into the ship's local frame: fore is the grid's +Y.
                var local = (-_transform.GetWorldRotation(grid)).RotateVec(direction);
                return ClassifyFacing(local);
            }
        }

        // No direction (explosions, ...): the strongest facing takes the hit.
        var best = -1;
        var bestCharge = 0f;
        for (var i = 0; i < 4; i++)
        {
            if (!shield.Up[i] || shield.Facings[i] <= bestCharge)
                continue;

            best = i;
            bestCharge = shield.Facings[i];
        }

        return best >= 0 ? (ShieldFacing) best : null;
    }

    private static ShieldFacing ClassifyFacing(Vector2 localDirection)
    {
        if (MathF.Abs(localDirection.X) > MathF.Abs(localDirection.Y))
            return localDirection.X > 0 ? ShieldFacing.Starboard : ShieldFacing.Port;

        return localDirection.Y > 0 ? ShieldFacing.Fore : ShieldFacing.Aft;
    }

    private float GetHardness(EntityUid grid)
    {
        if (!TryComp<ShipCombatComponent>(grid, out var combat))
            return 0f;

        return ShipPowerSystem.GetPool(combat, ShipPowerPool.Shields) * HardnessPerPoint;
    }

    private void SpawnImpactEffect(EntityUid target)
    {
        Spawn("ShieldImpactEffect", Transform(target).Coordinates);
    }

    private Vector2 GridCenter(EntityUid grid)
    {
        if (TryComp<PhysicsComponent>(grid, out var physics))
            return _transform.ToMapCoordinates(new EntityCoordinates(grid, physics.LocalCenter)).Position;

        return _transform.GetMapCoordinates(grid).Position;
    }
}
