using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._PS.Atmos.Components;
using Content.Shared._PS.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Construction.Components;
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
/// The maths is /tg/station's binary thermomachine, with three corrections the original needed for
/// Space Station 14:
/// <code>
/// C1      gas heat capacity, C2 machine heat capacity
/// rate    clamp((target - gas) * C1*C2/(C1+C2), -MaxHeatRate, MaxHeatRate)
/// heat    rate * dt * efficiency, signed: negative means the gas loses it
/// </code>
/// <list type="number">
/// <item>The transfer is clamped by what the waste loop can actually supply or absorb, because a gas
/// mixture here is clamped to <see cref="Atmospherics.TCMB"/>..<see cref="Atmospherics.Tmax"/> and an
/// unclamped transfer would silently create or destroy the difference.</item>
/// <item>Heating takes the heat out of the waste loop instead of making it.</item>
/// <item>The compressor's waste heat is the electricity it just drew, so it cannot exceed it.</item>
/// </list>
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

        SubscribeLocalEvent<GasHeatPumpComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<GasHeatPumpComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        SubscribeLocalEvent<GasHeatPumpComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasHeatPumpComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    /// <summary>
    /// Better parts make the machine move more heat and keep working into a hotter waste loop.
    /// </summary>
    private void OnRefreshParts(Entity<GasHeatPumpComponent> ent, ref RefreshPartsEvent args)
    {
        var bins = args.PartRatings.GetValueOrDefault(ent.Comp.MachinePartHeatCapacity, 1f);
        var manipulators = args.PartRatings.GetValueOrDefault(ent.Comp.MachinePartEfficiency, 1f);

        // The machine's heat capacity scales with the bins. The exchange term below is dominated by
        // the gas's own heat capacity once the machine's is large, so capacity alone would leave the
        // bins with almost nothing to do; the transfer ceiling follows its own, flatter curve.
        var binMultiplier = MathF.Pow(2f, bins - 1f);

        ent.Comp.HeatCapacity = ent.Comp.BaseHeatCapacity * binMultiplier;

        var heatRate = ent.Comp.HeatRateByRating.Length == 0
            ? ent.Comp.BaseMaxHeatRate
            : HeatRateForRating(ent.Comp.HeatRateByRating, bins);

        ent.Comp.MaxHeatRate = heatRate;
        ent.Comp.PowerSoftCap = heatRate;

        // Better bins are a better compressor as well as a bigger one: the power moved per joule
        // drops with every rating, so the higher tiers move their heat more cheaply.
        ent.Comp.PowerPerHeat = ent.Comp.BasePowerPerHeat *
                                MathF.Pow(ent.Comp.PowerPerHeatPerBin, bins - 1f);

        ent.Comp.EfficiencyFloor = MathF.Min(ent.Comp.BaseEfficiencyFloor * manipulators,
            ent.Comp.MaxEfficiencyFloor);

        // Better manipulators are finer control as well as better efficiency: the compressor can
        // build a bigger temperature lift, so the range you can ask the machine for widens.
        var rangeRating = MathF.Max(0f, manipulators - 1f);
        ent.Comp.MinTemperature = MathF.Max(Atmospherics.TCMB,
            ent.Comp.BaseMinTemperature - ent.Comp.MinTemperaturePerRating * rangeRating);
        ent.Comp.MaxTemperature = MathF.Min(Atmospherics.Tmax,
            ent.Comp.BaseMaxTemperature + ent.Comp.MaxTemperaturePerRating * rangeRating);
        ent.Comp.TargetTemperature = Math.Clamp(ent.Comp.TargetTemperature,
            ent.Comp.MinTemperature, ent.Comp.MaxTemperature);

        Dirty(ent);
    }

    /// <summary>
    /// Reads the heat-rate curve at a (possibly fractional) part rating. Ratings are averages, so a
    /// machine with mixed bins lands between two entries.
    /// </summary>
    private static float HeatRateForRating(float[] rates, float rating)
    {
        var position = Math.Clamp(rating - 1f, 0f, rates.Length - 1f);
        var lower = Math.Min((int) MathF.Floor(position), rates.Length - 1);
        var upper = Math.Min(lower + 1, rates.Length - 1);

        return rates[lower] + (rates[upper] - rates[lower]) * (position - lower);
    }

    /// <summary>
    /// Lists what the installed parts changed in the machine's "Upgrades" examine entry, so a player
    /// who installs better bins and manipulators can see what they bought.
    /// </summary>
    private void OnUpgradeExamine(EntityUid uid, GasHeatPumpComponent component, UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("gas-heat-pump-upgrade-heat-capacity",
            component.HeatCapacity / component.BaseHeatCapacity);
        args.AddPercentageUpgrade("gas-heat-pump-upgrade-heat-rate",
            component.MaxHeatRate / component.BaseMaxHeatRate);
        args.AddPercentageUpgrade("gas-heat-pump-upgrade-efficiency",
            component.EfficiencyFloor / component.BaseEfficiencyFloor);
        args.AddPercentageUpgrade("gas-heat-pump-upgrade-power",
            component.PowerPerHeat / component.BasePowerPerHeat);
        args.AddPercentageUpgrade("gas-heat-pump-upgrade-min-temperature",
            component.MinTemperature / component.BaseMinTemperature);
        args.AddPercentageUpgrade("gas-heat-pump-upgrade-max-temperature",
            component.MaxTemperature / component.BaseMaxTemperature);
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
            Idle(ent);
            return;
        }

        var gas = gasNode.Air;
        var waste = wasteNode.Air;

        // Both ports on one pipeline, or a port that isn't hooked up to a pipeline at all.
        if (ReferenceEquals(gas, waste) || gas.Immutable || waste.Immutable)
        {
            Idle(ent);
            return;
        }

        comp.GasTemperature = gas.Temperature;
        comp.GasPressure = gas.Pressure;
        comp.WasteTemperature = waste.Temperature;
        comp.WastePressure = waste.Pressure;

        if (!comp.Enabled || !_power.IsPowered(ent.Owner))
        {
            Idle(ent);
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
        if (cooling && comp.WasteTemperature > comp.SafeTemperature && !CheckSafeties(ent))
            return;

        // Nothing to do when the gas already sits on the setpoint.
        if (MathF.Abs(delta) < 0.1f || gas.TotalMoles < 0.01f)
        {
            comp.Efficiency = 1f;
            Idle(ent);
            return;
        }

        // Without gas in the waste loop there is nothing to dump heat into, or take it from.
        if (waste.TotalMoles < 0.01f)
        {
            comp.Efficiency = 0f;
            Idle(ent);
            return;
        }

        var gasCapacity = _atmosphere.GetHeatCapacity(gas, true);
        var wasteCapacity = _atmosphere.GetHeatCapacity(waste, true);
        if (gasCapacity <= 0f || wasteCapacity <= 0f)
        {
            comp.Efficiency = 0f;
            Idle(ent);
            return;
        }

        var efficiency = GetEfficiency(comp, gas, waste, cooling);

        // Heat the machine would like to move this tick: a two-capacity exchange, capped so a big
        // temperature difference cannot move an unreasonable amount of heat at once.
        var exchange = gasCapacity * comp.HeatCapacity / (gasCapacity + comp.HeatCapacity);
        var rate = Math.Clamp(delta * exchange, -comp.MaxHeatRate, comp.MaxHeatRate);
        var exchanged = rate * efficiency * dt;

        // Never move more heat than either side can actually take: the mixtures clamp at TCMB/Tmax and
        // an over-sized transfer would silently create or destroy the difference.
        var headroom = MathF.Min(
            exchanged > 0f
                ? gasCapacity * MathF.Max(0f, Atmospherics.Tmax - gas.Temperature)
                : gasCapacity * MathF.Max(0f, gas.Temperature - Atmospherics.TCMB),
            exchanged > 0f
                ? wasteCapacity * MathF.Max(0f, waste.Temperature - Atmospherics.TCMB)
                : wasteCapacity * MathF.Max(0f, Atmospherics.Tmax - waste.Temperature));
        exchanged = Math.Clamp(exchanged, -headroom, headroom);

        var heatPerSecond = exchanged / MathF.Max(dt, 1e-4f);
        var load = GetPowerDraw(comp, heatPerSecond, efficiency);

        // While cooling, the machine's own draw ends up as heat in the waste loop. Heating does not
        // add motor heat, so the term only applies to the cooling branch.
        var motor = cooling && MathF.Abs(delta) >= comp.MotorHeatDeadband ? load * dt : 0f;

        _atmosphere.AddHeat(gas, exchanged);
        _atmosphere.AddHeat(waste, -exchanged + motor);

        comp.Efficiency = efficiency;
        comp.HeatMoved = heatPerSecond;
        SetLoad(ent, load);
    }

    /// <summary>
    /// Handles the over-temperature waste loop. Returns false when the caller should stop processing.
    /// </summary>
    private bool CheckSafeties(Entity<GasHeatPumpComponent> ent)
    {
        var comp = ent.Comp;

        if (comp.Safeties)
        {
            comp.Enabled = false;
            Dirty(ent);
            UpdateAppearance(ent);
            UpdateUi(ent);
            _popup.PopupEntity(Loc.GetString("gas-heat-pump-safeties-tripped"), ent, PopupType.MediumCaution);
            Idle(ent);
            return false;
        }

        if (_timing.CurTime < comp.NextDetonationCheck)
            return false;

        comp.NextDetonationCheck = _timing.CurTime + comp.DetonationCheckInterval;
        Detonate(ent);
        return false;
    }

    /// <summary>
    /// How well the machine can move heat right now, from 0 to 1. Cooling is the hard direction: the
    /// hotter the waste loop already is, the less of the heat it will take. Either direction is
    /// strangled by a nearly empty pipe, because gas is what carries the heat.
    /// </summary>
    private static float GetEfficiency(GasHeatPumpComponent comp, GasMixture gas, GasMixture waste, bool cooling)
    {
        var efficiency = 1f;

        if (cooling)
        {
            // Log10 is already 0 at 1 K of difference, so the penalty never starts negative.
            var difference = MathF.Max(1f, waste.Temperature - gas.Temperature);
            efficiency = Math.Clamp(1f - MathF.Log10(difference) * 0.08f, 0.65f, 1f);

            // Parts decide how far the machine can degrade before it stops caring.
            efficiency = MathF.Max(efficiency, comp.EfficiencyFloor);
        }

        // The mole penalty applies after the parts floor, so good parts cannot ignore an empty pipe.
        return efficiency * MathF.Min(MoleEfficiency(comp, gas.TotalMoles), MoleEfficiency(comp, waste.TotalMoles));
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
        var load = comp.IdlePower
                   + heat * comp.PowerPerHeat * (1f + heat * comp.PowerSoftCapSlope / comp.PowerSoftCap);

        return load * (1.5f - 0.5f * efficiency);
    }

    /// <summary>
    /// Draws idle power while the machine is on, and nothing while it is off.
    /// </summary>
    private void Idle(Entity<GasHeatPumpComponent> ent)
    {
        var running = ent.Comp.Enabled && _power.IsPowered(ent.Owner);
        SetLoad(ent, running ? ent.Comp.IdlePower : 0f);
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
        var temperature = ent.Comp.WasteTemperature;
        if (temperature < ent.Comp.SafeTemperature + ent.Comp.DetonationMargin)
            return;

        // log6(500 000) * 10 is about 73%, and 6^10 is where it reaches 100%.
        var chance = Math.Clamp(MathF.Log(temperature, 6f) * 10f, 0f, 100f) / 100f;
        if (!_random.Prob(chance))
            return;

        _explosion.QueueExplosion(ent.Owner, ExplosionSystem.DefaultExplosionPrototypeId,
            3f, 1f, 3f, canCreateVacuum: false);

        QueueDel(ent);
    }
}
