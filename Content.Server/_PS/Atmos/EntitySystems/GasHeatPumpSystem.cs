using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._PS.Atmos.Components;
using Content.Shared._PS.Atmos.EntitySystems;
using Content.Shared.Construction.Components;
using Content.Shared.Atmos;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using JetBrains.Annotations;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: runs the heat pump. Heat is taken out of (or pushed into) the gas port and the same
/// amount is pushed into (or taken out of) the waste port, so no heat is created or destroyed.
/// </summary>
/// <remarks>
/// The maths is /tg/station's binary thermomachine, with the power draw rescaled to an SS14 APC:
/// <code>
/// C1      gas heat capacity, C2 machine heat capacity
/// heat    (target - gas) * C1*C2/(C1+C2) * dt
/// cooling the gas loses heat * efficiency, the waste loop gains that plus the compressor's own heat
/// heating the gas gains heat * efficiency, taken straight out of the waste loop
/// </code>
/// <para>
/// /tg/station only moved heat when cooling - a heating machine made heat out of nothing. This one
/// takes it from the waste loop in both directions, which is what a heat pump is, and is why the
/// waste loop is never optional.
/// </para>
/// Efficiency is what makes it a machine and not a magic heat deleter: it falls as the waste loop
/// runs hotter than the gas, and it collapses if either side has almost no gas in it.
/// </remarks>
[UsedImplicitly]
public sealed class GasHeatPumpSystem : SharedGasHeatPumpSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasHeatPumpComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasHeatPumpComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<GasHeatPumpComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    /// <summary>
    /// Better parts make the machine move more heat and keep working into a hotter waste loop.
    /// </summary>
    private void OnRefreshParts(Entity<GasHeatPumpComponent> ent, ref RefreshPartsEvent args)
    {
        var bins = args.PartRatings.GetValueOrDefault(ent.Comp.MachinePartHeatCapacity, 1f);
        var lasers = args.PartRatings.GetValueOrDefault(ent.Comp.MachinePartEfficiency, 1f);

        ent.Comp.HeatCapacity = ent.Comp.BaseHeatCapacity * MathF.Pow(2f, bins - 1f);
        ent.Comp.EfficiencyFloor = MathF.Min(ent.Comp.BaseEfficiencyFloor * lasers, 1f);
    }

    private void OnMapInit(Entity<GasHeatPumpComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUiUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
    }

    /// <summary>
    /// Refreshes the networked readings used by the window and examine once per second.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GasHeatPumpComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextUiUpdate > _timing.CurTime)
                continue;

            comp.NextUiUpdate += comp.UpdateInterval;

            Dirty(uid, comp);
            UpdateUi((uid, comp));
        }
    }

    private void OnAtmosUpdate(Entity<GasHeatPumpComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        var comp = ent.Comp;
        comp.HeatMoved = 0f;

        if (!_nodeContainer.TryGetNodes(ent.Owner, comp.GasPortName, comp.WastePortName,
                out PipeNode? gasNode, out PipeNode? wasteNode))
        {
            SetLoad(ent, comp.IdlePower);
            return;
        }

        var gas = gasNode.Air;
        var waste = wasteNode.Air;

        // Both ports on one pipeline, or a port that isn't hooked up to a pipeline at all.
        if (ReferenceEquals(gas, waste) || gas.Immutable || waste.Immutable)
        {
            SetLoad(ent, comp.IdlePower);
            return;
        }

        comp.GasTemperature = gas.Temperature;
        comp.GasPressure = gas.Pressure;
        comp.WasteTemperature = waste.Temperature;
        comp.WastePressure = waste.Pressure;

        if (!comp.Enabled || !_power.IsPowered(ent.Owner))
        {
            SetLoad(ent, comp.IdlePower);
            return;
        }

        var dt = args.dt;
        var delta = comp.TargetTemperature - gas.Temperature;

        // The machine is a heat pump: it decides by itself which way the heat has to go.
        var cooling = delta <= 0f;
        if (cooling != comp.Cooling)
        {
            comp.Cooling = cooling;
            Dirty(ent);
            UpdateAppearance(ent);
        }

        // A waste loop this hot means the safeties are the only thing between the machine and a hole in
        // the ship.
        if (cooling && comp.WasteTemperature > comp.SafeTemperature)
        {
            if (comp.Safeties)
            {
                comp.Enabled = false;
                Dirty(ent);
                UpdateAppearance(ent);
                UpdateUi(ent);
                _popup.PopupEntity(Loc.GetString("gas-heat-pump-safeties-tripped"), ent, PopupType.MediumCaution);
                SetLoad(ent, comp.IdlePower);
                return;
            }

            Detonate(ent);
            return;
        }

        // Nothing to do when the gas already sits on the setpoint.
        if (MathF.Abs(delta) < 0.1f || gas.TotalMoles < 0.01f)
        {
            comp.Efficiency = 1f;
            SetLoad(ent, comp.IdlePower);
            return;
        }

        // Without gas in the waste loop there is nothing to dump heat into, or take it from.
        if (waste.TotalMoles < 0.01f)
        {
            comp.Efficiency = 0f;
            SetLoad(ent, comp.IdlePower);
            return;
        }

        var gasCapacity = _atmosphere.GetHeatCapacity(gas, true);
        if (gasCapacity <= 0f)
        {
            comp.Efficiency = 0f;
            SetLoad(ent, comp.IdlePower);
            return;
        }

        // Heat the machine can move this tick, before efficiency.
        var exchange = gasCapacity * comp.HeatCapacity / (gasCapacity + comp.HeatCapacity);
        var heat = delta * exchange * dt;

        var efficiency = GetEfficiency(comp, gas, waste, cooling);
        comp.Efficiency = efficiency;
        comp.HeatMoved = heat * efficiency / MathF.Max(dt, 1e-4f);

        // Signed: negative means the gas loses the heat and the waste loop gains it.
        var exchanged = heat * efficiency;

        // The compressor's own waste heat only exists while it is working hard, so the machine can
        // settle on its setpoint instead of always trickling heat into the loop.
        var motor = cooling && MathF.Abs(delta) >= comp.MotorHeatDeadband ? comp.MotorHeat * dt : 0f;

        _atmosphere.AddHeat(gas, exchanged);
        _atmosphere.AddHeat(waste, -exchanged + motor);

        SetLoad(ent, GetPowerDraw(comp, comp.HeatMoved, efficiency));
    }

    /// <summary>
    /// How well the machine can move heat right now, from 0 to 1. Cooling is the hard direction: the
    /// hotter the waste loop already is, the less of the heat it will take, and a nearly empty pipe on
    /// either side strangles the exchange.
    /// </summary>
    private static float GetEfficiency(GasHeatPumpComponent comp, GasMixture gas, GasMixture waste, bool cooling)
    {
        if (!cooling)
            return 1f;

        // Log10 is already 0 at 1 K of difference, so the penalty never starts negative.
        var difference = MathF.Max(1f, waste.Temperature - gas.Temperature);
        var efficiency = Math.Clamp(1f - MathF.Log10(difference) * 0.08f, 0.65f, 1f);

        efficiency *= MathF.Min(MoleEfficiency(comp, gas.TotalMoles), MoleEfficiency(comp, waste.TotalMoles));

        return MathF.Max(efficiency, comp.EfficiencyFloor);
    }

    /// <summary>
    /// A side with almost nothing in it cannot carry heat away, so the machine slows down.
    /// </summary>
    private static float MoleEfficiency(GasHeatPumpComponent comp, float moles)
    {
        if (moles < comp.MinimumMoles)
            return comp.EmptySideEfficiency;

        return MathF.Max(1f - comp.MinimumMoles / (moles + 1f), comp.EmptySideEfficiency);
    }

    /// <summary>
    /// Input power in watts. The cost per joule rises with the amount of heat moved, so a big job is
    /// disproportionately expensive, and a badly running machine costs more for the same result.
    /// </summary>
    private static float GetPowerDraw(GasHeatPumpComponent comp, float heatPerSecond, float efficiency)
    {
        var heat = MathF.Abs(heatPerSecond);
        var load = comp.IdlePower + heat * comp.PowerPerHeat * (1f + heat / comp.PowerSoftCap);

        return load * (1.5f - 0.5f * efficiency);
    }

    private void SetLoad(Entity<GasHeatPumpComponent> ent, float load)
    {
        if (!TryComp(ent.Owner, out ApcPowerReceiverComponent? receiver))
            return;

        receiver.Load = load;
    }

    /// <summary>
    /// The waste loop went past the point of no return with the safeties off: /tg/station gave the
    /// machine a chance of blowing up that grows with how hot the loop is.
    /// </summary>
    /// <remarks>
    /// Unlike /tg/station the gas is left where it is. There the machine held the mixtures, so
    /// exploding had to vent them; here they live in the pipe network, which survives the machine.
    /// </remarks>
    private void Detonate(Entity<GasHeatPumpComponent> ent)
    {
        // 75% at the safe temperature, 100% at a hundred million kelvin.
        var chance = Math.Clamp(MathF.Log(ent.Comp.WasteTemperature, 6f) * 10f, 0f, 100f) / 100f;
        if (!_random.Prob(chance))
            return;

        _explosion.QueueExplosion(ent.Owner, ExplosionSystem.DefaultExplosionPrototypeId,
            3f, 1f, 3f, canCreateVacuum: false);

        QueueDel(ent);
    }
}
