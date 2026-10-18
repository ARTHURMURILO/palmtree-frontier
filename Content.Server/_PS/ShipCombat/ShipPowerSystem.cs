using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Construction.Components;
using Content.Shared._PS.ShipCombat;
using Content.Shared.Examine;
using Content.Shared.NodeContainer;
using Content.Shared._PS.ShipCombat.Components;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: the ship's bluespace power budget.
/// </summary>
/// <remarks>
/// The engineering console's four pools (Shields, Weapons, Thrusters, Auxiliary) are an STO-adapted
/// 200-point budget: each pool is 15-125 and they sum to 200. This system distributes the sink's
/// rated throughput across the pools, powers remote devices without wires, scales bluespace
/// thrusters with the Thrusters pool and returns the Auxiliary share to the ship's power net. The
/// sink's battery buffers the generator output; when it runs dry everything browns out proportionally.
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
        ShipPowerPool.Auxiliary,
    };

    private static readonly ShipPowerPool[] DevicePools =
    {
        ShipPowerPool.Shields,
        ShipPowerPool.Weapons,
        ShipPowerPool.Thrusters,
    };

    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly ThrusterSystem _thruster = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(PowerNetSystem));

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
            UpdateGrid(grid, combat, frameTime);
        }
    }

    /// <summary>
    /// The performance multiplier a pool gives its devices: 100% at the pool's rated point, less
    /// when underpowered, more when overpowered. 0 when the pool cannot operate.
    /// </summary>
    public static float GetPoolPerformance(ShipPowerPool pool, int value)
    {
        if (value < MinOperatingPool)
            return 0f;

        return pool switch
        {
            ShipPowerPool.Shields => value / 50f,
            ShipPowerPool.Weapons => (value + 100f) / 200f,
            ShipPowerPool.Thrusters => 1f + (value - 50f) * 0.01f,
            _ => 1f,
        };
    }

    public static int GetPool(ShipCombatComponent combat, ShipPowerPool pool)
    {
        return pool switch
        {
            ShipPowerPool.Shields => combat.ShieldPower,
            ShipPowerPool.Weapons => combat.WeaponPower,
            ShipPowerPool.Thrusters => combat.ThrusterPower,
            _ => combat.AuxiliaryPower,
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
            case ShipPowerPool.Thrusters:
                combat.ThrusterPower = value;
                break;
            default:
                combat.AuxiliaryPower = value;
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
            _ => combat.AuxiliaryPowerLocked,
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
            case ShipPowerPool.Thrusters:
                combat.ThrusterPowerLocked = locked;
                break;
            default:
                combat.AuxiliaryPowerLocked = locked;
                break;
        }
    }

    /// <summary>
    /// Adjusts one pool and moves the difference to the other unlocked pools proportionally, so the
    /// four pools always sum to <see cref="PoolTotal"/>.
    /// </summary>
    public void SetPool(Entity<ShipCombatComponent> combat, ShipPowerPool pool, int value)
    {
        NormalizePools(combat.Comp);

        var others = new List<ShipPowerPool>();
        foreach (var other in AllPools)
        {
            if (other == pool || IsPoolLocked(combat.Comp, other))
                continue;

            others.Add(other);
        }

        var current = GetPool(combat.Comp, pool);
        var delta = Math.Clamp(value, PoolMin, PoolMax) - current;
        if (delta == 0 || others.Count == 0)
            return;

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
    /// Applies a preset split. Presets override locked pools, like the presets in STO.
    /// </summary>
    public void ApplyPreset(Entity<ShipCombatComponent> combat, ShipPowerPreset preset)
    {
        switch (preset)
        {
            case ShipPowerPreset.Attack:
                SetPools(combat.Comp, 50, 100, 25, 25);
                break;
            case ShipPowerPreset.Defense:
                SetPools(combat.Comp, 100, 50, 25, 25);
                break;
            default:
                SetPools(combat.Comp, 50, 50, 50, 50);
                break;
        }
    }

    /// <summary>
    /// Clamps the pools into range and keeps their sum at <see cref="PoolTotal"/>.
    /// </summary>
    public void NormalizePools(ShipCombatComponent combat)
    {
        var sum = 0;
        foreach (var pool in AllPools)
        {
            var value = Math.Clamp(GetPool(combat, pool), PoolMin, PoolMax);
            SetPoolValue(combat, pool, value);
            sum += value;
        }

        var diff = PoolTotal - sum;
        while (diff != 0)
        {
            var changed = false;
            foreach (var pool in AllPools)
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

    /// <summary>
    /// Explains the sink's wiring, charge and budget, and warns when its input and output share a
    /// segment (which makes the sink self-charge and never feed the ship).
    /// </summary>
    private void OnSinkExamined(EntityUid uid, BluespacePowerSinkComponent sink, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (TryComp<BatteryComponent>(uid, out var battery))
        {
            args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-charge",
                ("charge", battery.CurrentCharge / 1000f),
                ("max", battery.MaxCharge / 1000f)));
        }

        args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-budget",
            ("throughput", sink.RatedThroughput / 1000f)));

        args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-wiring"));

        if (IsInputOutputSameNet(uid))
            args.PushMarkup(Loc.GetString("bluespace-power-sink-examine-same-net"));
    }

    private bool IsInputOutputSameNet(EntityUid uid)
    {
        if (!TryComp<NodeContainerComponent>(uid, out var container))
            return false;

        if (!container.Nodes.TryGetValue("input", out var input)
            || !container.Nodes.TryGetValue("output", out var output))
            return false;

        return input.NodeGroup != null && ReferenceEquals(input.NodeGroup, output.NodeGroup);
    }

    private static void SetPools(ShipCombatComponent combat, int shields, int weapons, int thrusters, int auxiliary)    {
        combat.ShieldPower = shields;
        combat.WeaponPower = weapons;
        combat.ThrusterPower = thrusters;
        combat.AuxiliaryPower = auxiliary;
    }

    private void UpdateGrid(EntityUid grid, ShipCombatComponent combat, float frameTime)
    {
        // The sinks on this grid: their rated throughput is the power budget.
        var sinks = new List<(EntityUid Uid, BluespacePowerSinkComponent Sink, BatteryComponent Battery)>();
        var throughput = 0f;

        var sinkQuery = EntityQueryEnumerator<BluespacePowerSinkComponent, BatteryComponent, TransformComponent>();
        while (sinkQuery.MoveNext(out var sinkUid, out var sink, out var battery, out var sinkXform))
        {
            if (sinkXform.GridUid != grid)
                continue;

            sinks.Add((sinkUid, sink, battery));
            throughput += sink.RatedThroughput;
        }

        // The remote devices on this grid.
        var devices = new List<(EntityUid Uid, BluespacePoweredComponent Powered, ApcPowerReceiverComponent Receiver)>();
        var deviceQuery = EntityQueryEnumerator<BluespacePoweredComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (deviceQuery.MoveNext(out var deviceUid, out var powered, out var receiver, out var deviceXform))
        {
            if (deviceXform.GridUid != grid)
                continue;

            devices.Add((deviceUid, powered, receiver));
        }

        if (sinks.Count == 0 || throughput <= 0f)
        {
            foreach (var (uid, powered, receiver) in devices)
                SetDevicePower(uid, powered, receiver, 0f);

            return;
        }

        NormalizePools(combat);

        // Performance and demand per pool.
        var performance = new float[4];
        var demand = new float[4];
        foreach (var pool in DevicePools)
        {
            performance[(int) pool] = GetPoolPerformance(pool, GetPool(combat, pool));
        }

        foreach (var (_, powered, _) in devices)
        {
            var poolPerformance = performance[(int) powered.Pool];
            if (poolPerformance <= 0f)
                continue;

            demand[(int) powered.Pool] += powered.RatedPower * poolPerformance;
        }

        // Each pool's share of the budget, limited to what its devices actually need.
        var allocation = new float[4];
        var totalDraw = 0f;
        foreach (var pool in DevicePools)
        {
            var share = GetPool(combat, pool) / (float) PoolTotal * throughput;
            allocation[(int) pool] = MathF.Min(share, demand[(int) pool]);
            totalDraw += allocation[(int) pool];
        }

        // The battery has to cover this tick's draw.
        var charge = 0f;
        foreach (var (_, _, battery) in sinks)
            charge += battery.CurrentCharge;

        var availableThisTick = frameTime > 0f ? charge / frameTime : 0f;
        var brownout = totalDraw > 0f ? Math.Clamp(availableThisTick / totalDraw, 0f, 1f) : 1f;

        var used = 0f;
        foreach (var (uid, powered, receiver) in devices)
        {
            var poolPerformance = performance[(int) powered.Pool];
            if (poolPerformance <= 0f)
            {
                SetDevicePower(uid, powered, receiver, 0f);
                continue;
            }

            var supply = brownout;
            var poolDemand = demand[(int) powered.Pool];
            if (poolDemand > 0f)
                supply *= MathF.Min(1f, allocation[(int) powered.Pool] / poolDemand);

            var effective = poolPerformance * supply;
            SetDevicePower(uid, powered, receiver, effective);
            used += powered.RatedPower * effective * frameTime;
        }

        DrainBatteries(sinks, used);

        // Whatever the Auxiliary pool holds is fed back to the ship's power net.
        var auxShare = GetPool(combat, ShipPowerPool.Auxiliary) / (float) PoolTotal;
        foreach (var (sinkUid, sink, _) in sinks)
        {
            if (!TryComp<PowerNetworkBatteryComponent>(sinkUid, out var netBattery))
                continue;

            netBattery.MaxSupply = auxShare * sink.RatedThroughput;
        }
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

    private void DrainBatteries(
        List<(EntityUid Uid, BluespacePowerSinkComponent Sink, BatteryComponent Battery)> sinks,
        float joules)
    {
        if (joules <= 0f)
            return;

        var charge = 0f;
        foreach (var (_, _, battery) in sinks)
            charge += battery.CurrentCharge;

        if (charge <= 0f)
            return;

        foreach (var (uid, _, battery) in sinks)
        {
            var share = battery.CurrentCharge / charge;
            _battery.TryUseCharge(uid, joules * share, battery);
        }
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
