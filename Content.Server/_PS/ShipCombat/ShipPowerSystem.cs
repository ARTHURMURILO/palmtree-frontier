using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Nodes;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Construction.Components;
using Content.Shared._PS.ShipCombat;
using Content.Shared.Examine;
using Content.Shared.NodeContainer;
using Content.Shared._PS.CCVar;
using Content.Shared._PS.ShipCombat.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: the ship's bluespace power budget.
/// </summary>
/// <remarks>
/// The engineering console's three pools (Shields, Weapons, Thrusters) are an STO-adapted 150-point
/// budget: each pool is 15-125 and the active pools sum to 150. The Bluespace Power Relay is a grid
/// consumer whose draw is the combined demand of the active pools; it powers the ship's weapons,
/// shields and bluespace thrusters remotely. Shield generators draw a fixed 10 kW each plus 0.6 kW
/// per regenerated hit point; weapons and torpedoes add transient per-shot draws. A grid brownout
/// throttles every remote device, and the relay never feeds power back into the ship's grid.
/// </remarks>
public sealed class ShipPowerSystem : EntitySystem
{
    public const int PoolMin = ShipPowerConstants.PoolMin;
    public const int PoolMax = ShipPowerConstants.PoolMax;
    public const int PoolTotal = ShipPowerConstants.PoolTotal;

    /// <summary>Pools below this value cannot run their devices (the `(!)` alerts).</summary>
    public const int MinOperatingPool = ShipPowerConstants.MinOperatingPool;

    private static readonly ShipPowerPool[] AllPools =
    {
        ShipPowerPool.Shields,
        ShipPowerPool.Weapons,
        ShipPowerPool.Thrusters,
    };

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ShipCombatSystem _combat = default!;
    [Dependency] private readonly ThrusterSystem _thruster = default!;

    private ISawmill _sawmill = default!;

    /// <summary>Transmitters whose grid is currently too weak for the draw (runtime only).</summary>
    private readonly HashSet<EntityUid> _brownoutSinks = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("shipcombat");

        UpdatesAfter.Add(typeof(PowerNetSystem));

        SubscribeLocalEvent<BluespacePowerSinkComponent, MapInitEvent>(OnSinkMapInit,
            after: new[] { typeof(ThrusterSystem) });
        SubscribeLocalEvent<BluespacePowerSinkComponent, ExaminedEvent>(OnSinkExamined);

        // Thruster parts refresh after ThrusterSystem, so the captured value is the parts-scaled one.
        SubscribeLocalEvent<BluespaceThrusterComponent, MapInitEvent>(OnThrusterMapInit,
            after: new[] { typeof(ThrusterSystem) });
        SubscribeLocalEvent<BluespaceThrusterComponent, RefreshPartsEvent>(OnThrusterRefreshParts,
            after: new[] { typeof(ThrusterSystem) });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipCombatComponent>();
        while (query.MoveNext(out var grid, out var combat))
        {
            UpdateGrid(grid, combat);
        }
    }

    /// <summary>
    /// The performance multiplier a pool gives its devices: <c>pool / PoolTotal</c> (50 is one third,
    /// 125 is five sixths). 0 when the pool cannot operate.
    /// </summary>
    public static float GetPoolPerformance(int value)
    {
        if (value < MinOperatingPool)
            return 0f;

        return value / (float) PoolTotal;
    }

    public static int GetPool(ShipCombatComponent combat, ShipPowerPool pool)
    {
        return pool switch
        {
            ShipPowerPool.Shields => combat.ShieldPower,
            ShipPowerPool.Weapons => combat.WeaponPower,
            ShipPowerPool.Thrusters => combat.ThrusterPower,
            _ => combat.ShieldPower,
        };
    }

    public static void SetPoolValue(ShipCombatComponent combat, ShipPowerPool pool, int value)
    {
        value = Math.Clamp(value, PoolMin, PoolMax);

        switch (pool)
        {
            case ShipPowerPool.Shields:
                combat.ShieldPower = value;
                break;
            case ShipPowerPool.Weapons:
                combat.WeaponPower = value;
                break;
            default:
                combat.ThrusterPower = value;
                break;
        }
    }

    public static bool IsPoolLocked(ShipCombatComponent combat, ShipPowerPool pool)
    {
        return pool switch
        {
            ShipPowerPool.Shields => combat.ShieldPowerLocked,
            ShipPowerPool.Weapons => combat.WeaponPowerLocked,
            ShipPowerPool.Thrusters => combat.ThrusterPowerLocked,
            _ => combat.ShieldPowerLocked,
        };
    }

    public static void SetPoolLocked(ShipCombatComponent combat, ShipPowerPool pool, bool locked)
    {
        switch (pool)
        {
            case ShipPowerPool.Shields:
                combat.ShieldPowerLocked = locked;
                break;
            case ShipPowerPool.Weapons:
                combat.WeaponPowerLocked = locked;
                break;
            default:
                combat.ThrusterPowerLocked = locked;
                break;
        }
    }

    /// <summary>True when the ship has devices in the pool, so its slider is usable.</summary>
    public static bool IsPoolActive(ShipCombatComponent combat, ShipPowerPool pool)
    {
        return pool switch
        {
            ShipPowerPool.Shields => combat.ShieldPoolActive,
            ShipPowerPool.Weapons => combat.WeaponPoolActive,
            ShipPowerPool.Thrusters => combat.ThrusterPoolActive,
            _ => false,
        };
    }

    /// <summary>
    /// Adjusts one active pool and moves the difference to the other active, unlocked pools
    /// proportionally. Inactive pools keep their value and are ignored.
    /// </summary>
    public void SetPool(Entity<ShipCombatComponent> combat, ShipPowerPool pool, int value)
    {
        if (!IsPoolActive(combat.Comp, pool))
            return;

        NormalizePools(combat.Comp);

        var others = new List<ShipPowerPool>();
        foreach (var other in AllPools)
        {
            if (other == pool || !IsPoolActive(combat.Comp, other) || IsPoolLocked(combat.Comp, other))
                continue;

            others.Add(other);
        }

        var current = GetPool(combat.Comp, pool);
        var target = Math.Clamp(value, PoolMin, PoolMax);
        var delta = target - current;
        if (delta == 0)
            return;

        // A ship with a single active pool has nothing to trade points with.
        if (others.Count == 0)
        {
            SetPoolValue(combat.Comp, pool, target);
            return;
        }

        var othersTotal = 0f;
        foreach (var other in others)
            othersTotal += GetPool(combat.Comp, other);

        var applied = 0;
        for (var i = 0; i < others.Count; i++)
        {
            var other = others[i];
            var otherValue = GetPool(combat.Comp, other);

            int change;
            if (i == others.Count - 1)
            {
                // The last pool absorbs the rounding.
                change = delta - applied;
            }
            else
            {
                var share = othersTotal > 0f ? otherValue / othersTotal : 1f / others.Count;
                change = (int) (delta * share);
            }

            var newValue = Math.Clamp(otherValue - change, PoolMin, PoolMax);
            applied += otherValue - newValue;
            SetPoolValue(combat.Comp, other, newValue);
        }

        SetPoolValue(combat.Comp, pool, current + applied);
        NormalizePools(combat.Comp);
    }

    /// <summary>
    /// Applies a preset split. Presets only touch pools the ship actually has, so unavailable
    /// sliders (and their devices' absent systems) stay where they are.
    /// </summary>
    public void ApplyPreset(Entity<ShipCombatComponent> combat, ShipPowerPreset preset)
    {
        var (shields, weapons, thrusters) = preset switch
        {
            ShipPowerPreset.Attack => (15, 120, 15),
            ShipPowerPreset.Defense => (120, 15, 15),
            _ => (50, 50, 50),
        };

        if (combat.Comp.ShieldPoolActive)
            SetPoolValue(combat.Comp, ShipPowerPool.Shields, shields);
        if (combat.Comp.WeaponPoolActive)
            SetPoolValue(combat.Comp, ShipPowerPool.Weapons, weapons);
        if (combat.Comp.ThrusterPoolActive)
            SetPoolValue(combat.Comp, ShipPowerPool.Thrusters, thrusters);

        NormalizePools(combat.Comp);
    }

    /// <summary>
    /// Clamps every pool into range and keeps the active pools at <see cref="PoolTotal"/> when there
    /// are at least two of them. Inactive pools keep their value and are not normalized.
    /// </summary>
    public void NormalizePools(ShipCombatComponent combat)
    {
        var active = new List<ShipPowerPool>();
        var sum = 0;

        foreach (var pool in AllPools)
        {
            var value = Math.Clamp(GetPool(combat, pool), PoolMin, PoolMax);
            SetPoolValue(combat, pool, value);

            if (!IsPoolActive(combat, pool))
                continue;

            active.Add(pool);
            sum += value;
        }

        if (active.Count < 2)
            return;

        var diff = PoolTotal - sum;
        while (diff != 0)
        {
            var changed = false;
            foreach (var pool in active)
            {
                var value = GetPool(combat, pool);
                if (diff > 0 && value < PoolMax)
                {
                    SetPoolValue(combat, pool, value + 1);
                    diff--;
                    changed = true;
                }
                else if (diff < 0 && value > PoolMin)
                {
                    SetPoolValue(combat, pool, value - 1);
                    diff++;
                    changed = true;
                }

                if (diff == 0)
                    break;
            }

            if (!changed)
                break;
        }
    }

    private void OnSinkMapInit(EntityUid uid, BluespacePowerSinkComponent component, MapInitEvent args)
    {
        _combat.TryEnsureShipCombat(uid, out _);
    }

    /// <summary>
    /// Explains the transmitter's current draw, rated draw and wiring, and warns when it is an
    /// inactive backup.
    /// </summary>
    private void OnSinkExamined(EntityUid uid, BluespacePowerSinkComponent sink, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        TryComp<PowerConsumerComponent>(uid, out var consumer);

        if (consumer != null)
        {
            args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-draw",
                ("draw", MathF.Ceiling(consumer.DrawRate / 1000f))));
        }

        args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-budget",
            ("throughput", sink.RatedThroughput / 1000f)));

        if (consumer?.Net is { IsConnectedNetwork: true } net)
        {
            args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-grid",
                ("supply", MathF.Ceiling(net.NetworkNode.LastCombinedMaxSupply / 1000f)),
                ("received", MathF.Ceiling(consumer.ReceivedPower / 1000f))));
        }

        args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-wiring"));

        if (!sink.Active)
        {
            // Distinguish "a second transmitter is the active one" from "this one was never wired
            // to a proper net": both used to read as standby, which hid wiring mistakes.
            if (consumer?.Net is { IsConnectedNetwork: true })
                args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-standby"));
            else
                args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-unconnected"));
        }
        else if (consumer != null && consumer.DrawRate > 0f && consumer.ReceivedPower <= 0.1f)
        {
            args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-nopower"));
        }
    }

    /// <summary>
    /// True when the grid has at least one device with <typeparamref name="T"/>.
    /// </summary>
    private bool HasDevice<T>(EntityUid grid) where T : Component
    {
        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out _, out var xform))
        {
            if (xform.GridUid == grid)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Attaches the transmitter's consumer to the HV cable on its own tile. This is also the fallback
    /// for ships saved with an older transmitter node type.
    /// </summary>
    private void EnsureCableUnderNet(EntityUid uid, PowerConsumerComponent consumer, EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var xform = Transform(uid);
        if (!xform.Anchored)
            return;

        var tile = gridComp.TileIndicesFor(xform.Coordinates);
        var nodeQuery = GetEntityQuery<NodeContainerComponent>();

        foreach (var cableUid in gridComp.GetAnchoredEntities(tile))
        {
            if (!nodeQuery.TryGetComponent(cableUid, out var container))
                continue;

            foreach (var node in container.Nodes.Values)
            {
                if (node is not CableNode { NodeGroup: IBasePowerNet net } || !net.IsConnectedNetwork)
                    continue;

                if (!ReferenceEquals(consumer.Net, net))
                    consumer.Net = net;

                return;
            }
        }
    }

    /// <summary>
    /// Picks the transmitter that draws for the grid. Extra transmitters stay as inactive backups:
    /// the first one on a connected, supplied power net wins, otherwise the first connected one.
    /// </summary>
    private static EntityUid? SelectTransmitter(
        List<(EntityUid Uid, BluespacePowerSinkComponent Sink, PowerConsumerComponent Consumer)> sinks)
    {
        sinks.Sort((a, b) => a.Uid.CompareTo(b.Uid));

        EntityUid? fallback = null;
        foreach (var (uid, _, consumer) in sinks)
        {
            if (consumer.Net is not { IsConnectedNetwork: true } net)
                continue;

            fallback ??= uid;
            if (net.NetworkNode.LastCombinedMaxSupply > 0f)
                return uid;
        }

        return fallback;
    }

    private void UpdateGrid(EntityUid grid, ShipCombatComponent combat)
    {
        // Which pools the ship actually has. Recomputed every tick so refitted ships adjust.
        combat.ShieldPoolActive = HasDevice<ShieldGeneratorComponent>(grid);
        combat.WeaponPoolActive = HasDevice<ShipWeaponComponent>(grid) || HasDevice<ShipTorpedoLauncherComponent>(grid);
        combat.ThrusterPoolActive = HasDevice<BluespaceThrusterComponent>(grid);

        NormalizePools(combat);

        // Transmitters on this grid, and the remote devices they feed.
        var sinks = new List<(EntityUid Uid, BluespacePowerSinkComponent Sink, PowerConsumerComponent Consumer)>();
        var sinkQuery = EntityQueryEnumerator<BluespacePowerSinkComponent, PowerConsumerComponent, TransformComponent>();
        while (sinkQuery.MoveNext(out var sinkUid, out var sink, out var consumer, out var sinkXform))
        {
            if (sinkXform.GridUid != grid)
                continue;

            // The transmitter is meant to be wired with an HV cable under it. Re-attach the consumer
            // to that cable's net explicitly so ships saved before the input-node change still work.
            EnsureCableUnderNet(sinkUid, consumer, grid);
            sinks.Add((sinkUid, sink, consumer));
        }

        var devices = new List<(EntityUid Uid, BluespacePoweredComponent Powered, ApcPowerReceiverComponent Receiver)>();
        var deviceQuery = EntityQueryEnumerator<BluespacePoweredComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (deviceQuery.MoveNext(out var deviceUid, out var powered, out var receiver, out var deviceXform))
        {
            if (deviceXform.GridUid == grid)
                devices.Add((deviceUid, powered, receiver));
        }

        // Performance and power demand per pool.
        var performance = new float[AllPools.Length];
        var demand = new float[AllPools.Length];
        foreach (var pool in AllPools)
            performance[(int) pool] = GetPoolPerformance(GetPool(combat, pool));

        var shieldMaintenancePower = _cfg.GetCVar(PSCVars.ShipCombatShieldMaintenancePower) * 1000f;

        foreach (var (uid, powered, _) in devices)
        {
            var poolPerformance = performance[(int) powered.Pool];
            if (poolPerformance <= 0f)
                continue;

            // Shield maintenance is a fixed per-generator draw; rated draw scales with pool
            // performance (thrusters). Weapons use transient per-shot costs instead of either.
            var passive = HasComp<ShieldGeneratorComponent>(uid) ? shieldMaintenancePower : 0f;
            demand[(int) powered.Pool] += passive + powered.RatedPower * poolPerformance;
        }

        // Shield regeneration is charged as a fixed draw until the next regeneration pulse.
        if (performance[(int) ShipPowerPool.Shields] > 0f)
            demand[(int) ShipPowerPool.Shields] += combat.ShieldRegenPowerDraw;

        var actionDraw = _timing.CurTime < combat.ActionPowerDrawUntil ? combat.ActionPowerDraw : 0f;
        if (actionDraw <= 0f)
            combat.ActionPowerDraw = 0f;

        var totalDraw = actionDraw;
        foreach (var pool in AllPools)
            totalDraw += demand[(int) pool];

        // Exactly one transmitter draws for the ship; the rest are inactive backups.
        var activeUid = SelectTransmitter(sinks);
        var activeConsumer = default(PowerConsumerComponent);
        foreach (var (uid, sink, consumer) in sinks)
        {
            sink.Active = uid == activeUid;
            consumer.DrawRate = sink.Active ? totalDraw : 0f;

            if (sink.Active)
                activeConsumer = consumer;
        }

        // Grid brownout: the transmitter only gets part of what it asked for, so every remote
        // device throttles by the same ratio. With no current draw there is nothing to brown out;
        // keep the devices powered so a weapons-only ship can fire its first shot.
        var supply = 0f;
        if (activeUid != null)
        {
            if (totalDraw > 0f && activeConsumer != null)
            {
                var load = activeConsumer.NetworkLoad;
                supply = Math.Clamp(load.ReceivingPower / totalDraw, 0f, 1f);
            }
            else
            {
                // Nothing is drawing, but the devices still need a live grid before they can fire.
                supply = activeConsumer?.Net is { IsConnectedNetwork: true } net
                         && net.NetworkNode.LastCombinedMaxSupply > 0f
                    ? 1f
                    : 0f;
            }
        }

        if (activeUid is { } active)
        {
            if (totalDraw > 0f && supply < 0.999f)
            {
                if (_brownoutSinks.Add(active))
                    _sawmill.Info($"{ToPrettyString(active)} is under-supplied: the ship's grid cannot cover the transmitter's draw");
            }
            else
            {
                _brownoutSinks.Remove(active);
            }
        }

        foreach (var (uid, powered, receiver) in devices)
        {
            var poolPerformance = performance[(int) powered.Pool];
            if (poolPerformance <= 0f)
            {
                SetDevicePower(uid, powered, receiver, 0f);
                continue;
            }

            SetDevicePower(uid, powered, receiver, poolPerformance * supply);
        }
    }

    /// <summary>
    /// Adds a transient relay draw for a weapon shot or torpedo launch. The draw lasts
    /// <c>ps.ship_combat.action_power_duration_seconds</c> from the last event.
    /// </summary>
    public void AddActionPowerDraw(EntityUid grid, float watts)
    {
        if (watts <= 0f || !TryComp<ShipCombatComponent>(grid, out var combat))
            return;

        var duration = MathF.Max(0f, _cfg.GetCVar(PSCVars.ShipCombatActionPowerDuration));
        combat.ActionPowerDraw += watts;
        combat.ActionPowerDrawUntil = _timing.CurTime + TimeSpan.FromSeconds(duration);
    }

    /// <summary>
    /// Applies a device's power state: toggles its receiver, records the performance and scales a
    /// bluespace thruster's thrust.
    /// </summary>
    private void SetDevicePower(EntityUid uid, BluespacePoweredComponent powered, ApcPowerReceiverComponent receiver, float performance)
    {
        var isPowered = performance > 0f;
        powered.Performance = performance;
        powered.Powered = isPowered;

        if (receiver.PowerDisabled == isPowered)
            receiver.PowerDisabled = !isPowered;

        if (TryComp<ThrusterComponent>(uid, out var thruster)
            && TryComp<BluespaceThrusterComponent>(uid, out var bluespaceThruster))
        {
            ApplyThrusterPerformance(uid, thruster, bluespaceThruster, performance);
        }
    }

    private void ApplyThrusterPerformance(EntityUid uid, ThrusterComponent thruster, BluespaceThrusterComponent bluespace, float performance)
    {
        // Fallback for saves written before PartsThrust existed.
        if (bluespace.PartsThrust <= 0f)
            bluespace.PartsThrust = thruster.Thrust;

        _thruster.SetThrust(uid, thruster, bluespace.PartsThrust * performance);
    }

    private void OnThrusterMapInit(EntityUid uid, BluespaceThrusterComponent component, MapInitEvent args)
    {
        if (TryComp<ThrusterComponent>(uid, out var thruster))
            component.PartsThrust = thruster.Thrust;
    }

    private void OnThrusterRefreshParts(EntityUid uid, BluespaceThrusterComponent component, RefreshPartsEvent args)
    {
        if (TryComp<ThrusterComponent>(uid, out var thruster))
            component.PartsThrust = thruster.Thrust;
    }
}
