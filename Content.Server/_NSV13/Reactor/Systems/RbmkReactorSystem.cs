// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._PS.Ships;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Emp;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Content.Shared.Audio;
using Content.Shared._NSV13.Reactor;
using Robust.Server.GameObjects;
using Content.Shared.Radiation.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     NSV13 RBMK reactor simulation.
/// </summary>
/// <remarks>
///     Faithful port of <c>rbmk.dm</c>'s <c>slowprocess()</c> and <c>handle_alerts()</c>. Ordering in
///     the original is load-bearing and is preserved deliberately — in particular:
///     <list type="bullet">
///     <item><c>Power</c> is computed <i>before</i> <c>K</c> is added to the temperature, so watts and
///     nucleium output lag the temperature by one tick.</item>
///     <item>Coolant heat exchange runs before moderator consumption and before the temperature rise.</item>
///     <item><c>K += total_fuel_moles/1000</c> happens even when the fuel gate is under its threshold.</item>
///     <item><c>if (!HasFuel()) K = 0</c> runs after that, so moderator alone can never sustain reactivity.</item>
///     </list>
///     The 2-second cadence is the real cadence of the original: its <c>next_slowprocess</c> gate is
///     <c>world.time + 1 SECONDS</c> but <c>SSmachines.wait</c> is 2 s, so the gate can never fire early.
///
///     Deliberate deviations are marked with <c>PORT NOTE</c>.
/// </remarks>
public sealed class RbmkReactorSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ShipEventSystem _shipEvent = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly RbmkInteractionSystem _interaction = default!;

    /// <summary>NSV13 vents with the clockcult steam whoosh; the file is already in this fork.</summary>
    private static readonly SoundPathSpecifier SteamWhooshSound = new("/Audio/Magic/Cults/ClockCult/steam_whoosh.ogg");

    private const float KelvinOffset = Atmospherics.T0C;
    private const float KPaToPsi = 0.145037738f;

    /// <summary>Baseline reactivity change per tick, matching <c>initial(control_rod_effectiveness)</c>.</summary>
    private const float ControlRodBase = 0.65f;

    /// <summary>Baseline fuel rod depletion per tick.</summary>
    private const float DepletionBase = 0.035f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RbmkReactorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<RbmkReactorComponent, EntInsertedIntoContainerMessage>(OnRodInserted);
        SubscribeLocalEvent<RbmkReactorComponent, EntRemovedFromContainerMessage>(OnRodRemoved);
    }

    /// <summary>
    ///     The fuel container is the single source of truth for <see cref="RbmkReactorComponent.FuelRods"/>.
    /// </summary>
    /// <remarks>
    ///     Keeping a second list in sync by hand broke every path that did not go through the
    ///     insertion DoAfter: <c>ContainerFill</c> on the pre-started prototype loaded five rods that
    ///     the tick could not see, so a "running" reactor sat at zero reactivity with a full core.
    ///     Listening to the container covers hand insertion, container fill and rod conversion alike.
    /// </remarks>
    private void OnRodInserted(EntityUid uid, RbmkReactorComponent reactor, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != RbmkReactorComponent.FuelContainer)
            return;

        if (!HasComp<RbmkFuelRodComponent>(args.Entity) || reactor.FuelRods.Contains(args.Entity))
            return;

        reactor.FuelRods.Add(args.Entity);

        // A rod sitting in the core is shielded by the core; the reactor's own (much smaller)
        // emission is the only thing the room should read. The rod's own source comes back when it
        // is ejected or installed into a suit.
        if (TryComp<RadiationSourceComponent>(args.Entity, out var rodRadiation))
            rodRadiation.Enabled = false;

        // "That was the first fuel rod. Let's heat it up." A reactor handed to the crew already
        // running keeps its setpoint; a cold one starts itself.
        if (reactor.FuelRods.Count == 1 && !reactor.IsRunning)
            StartUp(uid, reactor);
    }

    private void OnRodRemoved(EntityUid uid, RbmkReactorComponent reactor, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != RbmkReactorComponent.FuelContainer)
            return;

        reactor.FuelRods.Remove(args.Entity);

        if (TryComp<RadiationSourceComponent>(args.Entity, out var rodRadiation))
            rodRadiation.Enabled = true;
    }

    private void OnStartup(EntityUid uid, RbmkReactorComponent reactor, ComponentStartup args)
    {
        // PORT NOTE: the original randomises gas_absorption_effectiveness to exactly 0.5 or 0.6 on
        // init and snapshots it into gas_absorption_constant so every reactor is slightly different.
        // Robust's IRobustRandom (rather than System.Random) keeps this deterministic across a
        // replay/prediction run, which is why the reactor constant must not use Random.Shared.
        reactor.GasAbsorptionEffectiveness = _random.Next(5, 7) / 10f;
        reactor.GasAbsorptionConstant = reactor.GasAbsorptionEffectiveness;

        // A reactor mapped in already running never goes through StartUp(), so give it its hum too.
        if (reactor.IsRunning)
            _ambient.SetAmbience(uid, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RbmkReactorComponent>();

        while (query.MoveNext(out var uid, out var reactor))
        {
            if (!reactor.IsRunning || reactor.Slagged)
                continue;

            if (now < reactor.NextTick)
                continue;

            reactor.NextTick = now + TimeSpan.FromSeconds(reactor.TickInterval);

            // PORT NOTE: the original can abort the whole tick when the reactor has no cable node,
            // which makes an unwired reactor permanently inert and unable to melt down. That is a
            // footgun, so here a missing power net simply means no watts are exported.
            SlowProcess(uid, reactor);
        }
    }

    private void SlowProcess(EntityUid uid, RbmkReactorComponent reactor)
    {
        if (reactor.Slagged)
        {
            reactor.IsRunning = false;
            return;
        }

        if (!_nodeContainer.TryGetNodes(uid,
                reactor.CoolantInputName,
                reactor.ModeratorInputName,
                reactor.CoolantOutputName,
                out PipeNode? coolantIn,
                out PipeNode? moderatorIn,
                out PipeNode? coolantOut))
        {
            return;
        }

        var coolantInAir = coolantIn.Air;
        var moderatorAir = moderatorIn.Air;
        var coolantOutAir = coolantOut.Air;

        // Per-tick diagnosis. Cleared before anything can hurt the vessel so a stale cause from the
        // last tick cannot be blamed for this one.
        reactor.LastIntegrityCause = RbmkIntegrityCause.None;
        reactor.LastIntegrityDamage = 0f;
        reactor.LargestIntegrityHit = 0f;
        reactor.CoolantSatisfaction = 1f;

        if (reactor.Precharged)
            Recharge(reactor, coolantInAir, coolantOutAir, moderatorAir);

        // ---------------- COOLANT HEAT EXCHANGE ----------------
        var inputMoles = coolantInAir.TotalMoles;

        if (inputMoles >= reactor.MinimumCoolantLevel)
        {
            var inputCelsius = KelvinToCelsius(coolantInAir.Temperature);
            reactor.LastCoolantTemperature = inputCelsius;

            var heatDelta = inputCelsius / 100f * reactor.GasAbsorptionEffectiveness;
            reactor.LastHeatDelta = heatDelta;
            reactor.Temperature += heatDelta;

            // All coolant moves to the outlet, then the inlet is emptied.
            _atmosphere.Merge(coolantOutAir, coolantInAir);
            coolantInAir.Clear();

            reactor.NoCoolantTicks = Math.Max(0, reactor.NoCoolantTicks - 2);
            reactor.CoolantSatisfaction = 1f;
        }
        else if (HasFuel(reactor))
        {
            reactor.NoCoolantTicks++;

            // How much of the floor the inlet is actually meeting, for the console.
            reactor.CoolantSatisfaction = reactor.MinimumCoolantLevel <= 0f
                ? 1f
                : Math.Clamp(inputMoles / reactor.MinimumCoolantLevel, 0f, 1f);

            if (reactor.NoCoolantTicks > reactor.NoCoolantTolerance)
            {
                reactor.Temperature += reactor.Temperature / 500f;
                NoteIntegrityDamage(reactor, RbmkIntegrityCause.CoolantStarvation, reactor.Temperature / 200f);
            }
        }

        // ---------------- OUTPUT SIDE ----------------
        coolantOutAir.Temperature = CelsiusToKelvin(reactor.Temperature);
        reactor.LastOutputTemperature = KelvinToCelsius(coolantOutAir.Temperature);
        reactor.Pressure = coolantOutAir.Pressure * KPaToPsi;
        reactor.Power = reactor.Temperature / reactor.TemperatureCritical * 100f;

        var spice = 1f;
        var depletionModifier = DepletionBase;
        var powerProduced = 0f;

        // Reset before the moderator block can raise it again.
        reactor.GasAbsorptionEffectiveness = reactor.GasAbsorptionConstant;

        // ---------------- MODERATOR ----------------
        if (moderatorAir.TotalMoles >= reactor.MinimumCoolantLevel)
        {
            var moderatorMoles = moderatorAir.TotalMoles;

            var totalFuelMoles = moderatorAir.GetMoles(Gas.Plasma)
                                 + moderatorAir.GetMoles(Gas.ConstrictedPlasma) * 2f
                                 + moderatorAir.GetMoles(Gas.Tritium) * 10f;

            var oxygenModifier = Math.Max(moderatorAir.GetMoles(Gas.Oxygen) / moderatorMoles * 10f, 1f);

            if (totalFuelMoles >= reactor.MinimumCoolantLevel)
            {
                var fuelPowerFactor = Math.Max(totalFuelMoles / moderatorMoles * 10f, 1f);

                powerProduced = Math.Max(0f, fuelPowerFactor * oxygenModifier * moderatorMoles);
                powerProduced *= Math.Max(0f, reactor.Power) / 100f;
                powerProduced *= reactor.BasePowerModifier;

                spice += moderatorAir.GetMoles(Gas.Tritium) / 5f;

                if (reactor.Power >= 20f)
                    coolantOutAir.AdjustMoles(Gas.Nucleium, totalFuelMoles / 20f);
            }

            reactor.LastPowerProduced = powerProduced;

            if (TryComp<PowerSupplierComponent>(uid, out var supplier))
                supplier.MaxSupply = powerProduced;

            var totalControlMoles = moderatorAir.GetMoles(Gas.Nitrogen)
                                    + moderatorAir.GetMoles(Gas.CarbonDioxide) * 2f
                                    + moderatorAir.GetMoles(Gas.Pluoxium) * 3f;

            if (totalControlMoles >= reactor.MinimumCoolantLevel)
            {
                var controlBonus = totalControlMoles / 250f;
                reactor.ControlRodEffectiveness = ControlRodBase + controlBonus;
                spice += moderatorAir.GetMoles(Gas.Nitrogen) / 25f;
                spice += moderatorAir.GetMoles(Gas.CarbonDioxide) / 12.5f;
            }

            var totalPermeabilityMoles = moderatorAir.GetMoles(Gas.BZ)
                                         + moderatorAir.GetMoles(Gas.WaterVapor) * 2f
                                         + moderatorAir.GetMoles(Gas.HyperNoblium) * 10f;

            if (totalPermeabilityMoles >= reactor.MinimumCoolantLevel)
            {
                var permeabilityBonus = totalPermeabilityMoles / 500f;
                reactor.GasAbsorptionEffectiveness = reactor.GasAbsorptionConstant + permeabilityBonus;
            }

            var totalDegradationMoles = moderatorAir.GetMoles(Gas.Nitryl);

            if (totalDegradationMoles >= reactor.MinimumCoolantLevel * 0.5f)
                depletionModifier += totalDegradationMoles / 15f;

            // Everything left in the moderator gate is destroyed, used or not.
            moderatorAir.Clear();
            reactor.K += totalFuelMoles / 1000f;
        }
        else
        {
            reactor.LastPowerProduced = 0f;

            if (TryComp<PowerSupplierComponent>(uid, out var supplier))
                supplier.MaxSupply = 0f;
        }

        // ---------------- FUEL RODS ----------------
        var fuelPower = 0f;

        if (!HasFuel(reactor))
        {
            reactor.K = 0f;
        }
        else
        {
            // Iterate a copy: depleting a rod can transform it, mutating the list.
            foreach (var rodUid in reactor.FuelRods.ToArray())
            {
                if (!TryComp<RbmkFuelRodComponent>(rodUid, out var rod))
                {
                    reactor.FuelRods.Remove(rodUid);
                    continue;
                }

                reactor.K += rod.FuelPower;
                fuelPower += rod.FuelPower;
                Deplete(uid, rodUid, reactor, rod, depletionModifier);
            }
        }

        // ---------------- CONTROL RODS CHASE DESIRED_K ----------------
        var difference = Math.Abs(reactor.K - reactor.DesiredK);
        difference = Math.Clamp(difference, 0f, reactor.ControlRodEffectiveness);

        if (difference > fuelPower && reactor.DesiredK > reactor.K)
            difference = fuelPower;

        if (reactor.K != reactor.DesiredK)
        {
            if (reactor.DesiredK > reactor.K)
                reactor.K += difference;
            else if (reactor.DesiredK < reactor.K)
                reactor.K -= difference;
        }

        reactor.K = Math.Clamp(reactor.K, 0f, reactor.MaxCriticality);

        // ---------------- HEATING ----------------
        if (HasFuel(reactor))
            reactor.Temperature += reactor.K;
        else
            reactor.Temperature -= 10f;

        HandleAlerts(uid, reactor);
        UpdateRadiation(uid, reactor, spice);
        OverloadFlicker(uid, reactor);
        UpdateVisuals(uid, reactor);
        _interaction.ApplyContactEffects(uid, reactor);
    }

    /// <summary>
    ///     Ported from <c>handle_alerts()</c> (rbmk.dm:402-454).
    /// </summary>
    private void HandleAlerts(EntityUid uid, RbmkReactorComponent reactor)
    {
        var alert = false;

        if (reactor.K <= 0f && reactor.Temperature <= 0f)
            ShutDown(uid, reactor);

        if (reactor.Temperature >= reactor.TemperatureCritical)
        {
            alert = true;

            if (reactor.Temperature >= reactor.TemperatureMeltdown)
            {
                // min(temperature/100, initial(vessel_integrity)/40)
                var temperatureDamage = Math.Min(reactor.Temperature / 100f, reactor.MaxVesselIntegrity / 40f);
                NoteIntegrityDamage(reactor, RbmkIntegrityCause.OverTemperature, temperatureDamage);

                if (reactor.VesselIntegrity <= temperatureDamage)
                {
                    Meltdown(uid, reactor);
                    return;
                }
            }
        }

        // Cold clamp. The DM original also tinted the sprite cyan here; this fork's sprite component
        // is client-only, so the frost is reported on the console instead (PORT NOTE).
        if (reactor.Temperature < -200f)
            reactor.Temperature = -200f;

        if (reactor.Pressure >= reactor.PressureCritical)
        {
            alert = true;

            // min(pressure/100, initial(vessel_integrity)/45)
            var pressureDamage = Math.Min(reactor.Pressure / 100f, reactor.MaxVesselIntegrity / 45f);
            NoteIntegrityDamage(reactor, RbmkIntegrityCause.OverPressure, pressureDamage);

            // Vent steam into the room. This is the DM "woosh": every tick the core is over
            // pressure it hisses, shakes the deck and dumps water vapour into the compartment.
            var mix = _atmosphere.GetContainingMixture(uid, true, true);
            if (mix != null)
            {
                mix.AdjustMoles(Gas.WaterVapor, reactor.Pressure / 100f);
                mix.Temperature = CelsiusToKelvin(reactor.Temperature);
            }

            _audio.PlayPvs(SteamWhooshSound, uid);

            // Only the reactor compartment should feel this; a hissing core must not shake the
            // whole ship every tick.
            _shipEvent.ShakeLocal(uid, 8f, 0.6f);
            VentSteam(uid, reactor);

            if (reactor.VesselIntegrity <= pressureDamage)
            {
                Blowout(uid, reactor);
                return;
            }
        }

        // PORT NOTE: the original relays a looping alarm across the ship's overmap, which has no
        // SS14 equivalent. We keep the alert flag and arm the alarm sound locally instead.
        if (alert && !reactor.AlertActive)
        {
            reactor.AlertActive = true;
            StartAlarm(uid, reactor);
        }
        else if (!alert)
        {
            reactor.AlertActive = false;
            StopAlarm(reactor);
        }
    }

    /// <summary>
    ///     Apply structural damage and remember what caused it.
    /// </summary>
    /// <remarks>
    ///     Several hazards can hit on the same tick (a coolant-starved core is also an over-pressure
    ///     core). The cause kept is the one that did the most damage this tick, because that is the
    ///     one the engineer should deal with first; the total is accumulated so the console can show
    ///     a rate rather than a mystery.
    /// </remarks>
    private static void NoteIntegrityDamage(RbmkReactorComponent reactor, RbmkIntegrityCause cause, float amount)
    {
        if (amount <= 0f)
            return;

        reactor.VesselIntegrity -= amount;
        reactor.LastIntegrityDamage += amount;

        // Largest single source wins the label.
        if (amount < reactor.LargestIntegrityHit)
            return;

        reactor.LargestIntegrityHit = amount;
        reactor.LastIntegrityCause = cause;
    }

    /// <summary>
    ///     Raise the klaxon, and keep it up.
    /// </summary>
    /// <remarks>
    ///     The DM original hands the alarm to the ship's overmap as a looping sound; playing it once
    ///     with <c>PlayPvs</c> gave a one-second blip and then silence while the core was still
    ///     cooking itself. It loops until the alert clears (or the core is destroyed), which is what
    ///     makes a reactor alarm actually mean something.
    /// </remarks>
    private void StartAlarm(EntityUid uid, RbmkReactorComponent reactor)
    {
        StopAlarm(reactor);
        reactor.AlarmStream = _audio.PlayPvs(reactor.AlarmSound, uid, AudioParams.Default.WithLoop(true))?.Entity;
    }

    private void StopAlarm(RbmkReactorComponent reactor)
    {
        if (reactor.AlarmStream is not { } stream)
            return;

        reactor.AlarmStream = null;

        if (Exists(stream))
            _audio.Stop(stream);
    }

    /// <summary>
    ///     Stand-in plumbing for <see cref="RbmkReactorComponent.Precharged"/>: cold coolant arrives
    ///     at the inlet every tick and plasma is fed to the moderator gate.
    /// </summary>
    /// <remarks>
    ///     The coolant side has to be re-supplied rather than recirculated: the tick merges the whole
    ///     inlet mixture into the outlet, so without a heat exchanger the loop would be storing hot
    ///     gas and the core would run away. Restocking at a fixed inlet temperature is exactly what
    ///     a working radiator loop looks like from the core's point of view.
    /// </remarks>
    private void Recharge(RbmkReactorComponent reactor, GasMixture coolantIn, GasMixture coolantOut, GasMixture moderator)
    {
        coolantIn.Clear();
        coolantOut.Clear();
        coolantIn.AdjustMoles(Gas.Oxygen, reactor.PrechargeCoolantMoles);
        coolantIn.Temperature = CelsiusToKelvin(reactor.PrechargeCoolantCelsius);

        // Only top the gate up to the floor: the moderator block destroys whatever is left, so
        // refilling from empty every tick is the same as a pump with a fixed flow rate.
        if (moderator.TotalMoles < reactor.MinimumCoolantLevel)
        {
            moderator.AdjustMoles(Gas.Plasma, reactor.PrechargeModeratorMoles);
            moderator.Temperature = Atmospherics.T20C;
        }
    }

    /// <summary>
    ///     Spawn a couple of short-lived vapour puffs around the core. Purely cosmetic, but it is
    ///     what makes an over-pressure event readable from across the room instead of a number on a
    ///     console.
    /// </summary>
    private void VentSteam(EntityUid uid, RbmkReactorComponent reactor)
    {
        var coords = _transform.GetMapCoordinates(uid);
        var count = reactor.Pressure > reactor.PressureCritical * 1.5f ? 3 : 1;

        for (var i = 0; i < count; i++)
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(0f, 0.6f);
            Spawn(reactor.VentEffect, new MapCoordinates(coords.Position + offset, coords.MapId));
        }
    }

    /// <summary>
    ///     §C.1 <c>if(power >= 90 &amp;&amp; world.time >= next_flicker)</c>: an overloaded core makes the
    ///     ship's lights stutter every 1.5 minutes. Much shorter here, where a 2 s tick is the
    ///     cadence the crew actually experiences.
    /// </summary>
    /// <remarks>
    ///     Reuses <see cref="ShipEventSystem.BlowLights"/> with a zero destroy chance and a brief
    ///     duration: that helper also schedules the blinking to stop again. The earlier open-coded
    ///     version only ever turned blinking <em>on</em>, so one overload left the whole ship
    ///     strobing forever.
    /// </remarks>
    private void OverloadFlicker(EntityUid uid, RbmkReactorComponent reactor)
    {
        if (reactor.Power < 90f || _timing.CurTime < reactor.NextFlicker)
            return;

        reactor.NextFlicker = _timing.CurTime + reactor.FlickerInterval;
        _shipEvent.BlowLights(uid, destroyChance: 0f, flickerChance: 0.25f,
            flickerTime: TimeSpan.FromSeconds(2));
    }

    /// <summary>
    ///     §<c>update_icon()</c> (rbmk.dm:512-529): the six temperature bands, plus "no fuel" and
    ///     "slagged" overriding them. Routed through Appearance so the ported RSI animation plays.
    /// </summary>
    public void UpdateVisuals(EntityUid uid, RbmkReactorComponent reactor)
    {
        if (!HasComp<AppearanceComponent>(uid))
            return;

        var state = reactor.Slagged
            ? RbmkVisualState.Slagged
            : !reactor.IsRunning
                ? RbmkVisualState.Off
                : InternalBandState(reactor);

        _appearance.SetData(uid, RbmkVisuals.Core, state);
    }

    private static RbmkVisualState InternalBandState(RbmkReactorComponent reactor)
    {
        if (reactor.Temperature <= 0f)
            return RbmkVisualState.Off;

        if (reactor.Temperature < 200f)
            return RbmkVisualState.On;

        if (reactor.Temperature < reactor.TemperatureOperating)
            return RbmkVisualState.Hot;

        // DM hard-codes 750 here rather than using a define; kept as-is.
        if (reactor.Temperature < 750f)
            return RbmkVisualState.VeryHot;

        if (reactor.Temperature < reactor.TemperatureCritical)
            return RbmkVisualState.Overheat;

        return RbmkVisualState.Meltdown;
    }

    private void UpdateRadiation(EntityUid uid, RbmkReactorComponent reactor, float spice)
    {
        // PORT NOTE: the original calls radiation_pulse(temperature * spice) once per tick. SS14's
        // radiation model is continuous and unconditional, so the same figure has to be rescaled
        // (see RadiationPerDegree) and capped: an unclamped temperature readout is thousands of
        // rads per second, which deletes a character before the damage system can show a warning.
        if (TryComp<RadiationSourceComponent>(uid, out var source))
        {
            source.Intensity = Math.Clamp(reactor.Temperature * spice * reactor.RadiationPerDegree,
                0f,
                reactor.MaxCoreRadiation);
        }
    }

    /// <summary>
    ///     Ported from <c>fuel_rods.dm:67-71</c> / <c>depletion_final()</c> (lines 41-65): once a rod
    ///     hits its threshold it is replaced inside the reactor by its conversion product, carrying
    ///     the accumulated depletion across.
    /// </summary>
    private void Deplete(
        EntityUid reactorUid,
        EntityUid rodUid,
        RbmkReactorComponent reactor,
        RbmkFuelRodComponent rod,
        float modifier)
    {
        rod.Depletion += modifier * rod.DepletionSpeedModifier;

        if (rod.Depletion < rod.DepletionThreshold || rod.DepletedFinal)
            return;

        rod.DepletedFinal = true;

        if (rod.DepletionConversion is not { } conversion)
            return;

        if (!reactor.FuelRods.Contains(rodUid))
            return;

        var container = _container.EnsureContainer<Container>(reactorUid, RbmkReactorComponent.FuelContainer);
        var converted = Spawn(conversion.Id, Transform(reactorUid).Coordinates);

        if (!TryComp<RbmkFuelRodComponent>(converted, out var convertedRod) ||
            !_container.Insert(converted, container))
        {
            // Do not leave a stray rod on the floor inside the core if the swap could not happen.
            QueueDel(converted);
            return;
        }

        // The DM original copies `depletion` onto the replacement, so the new rod continues from
        // where the old one left off rather than starting fresh.
        convertedRod.Depletion = rod.Depletion;

        // Inserting the replacement registers it with the core; removing the spent rod unregisters
        // it. Both go through the container so the list can never drift from the contents.
        _container.Remove(rodUid, container);
        QueueDel(rodUid);
    }

    public bool HasFuel(RbmkReactorComponent reactor)
    {
        return reactor.FuelRods.Count > 0;
    }

    /// <summary>
    ///     Ported from <c>meltdown()</c> (rbmk.dm:458-488), retargeted from "the station" to
    ///     "the ship the reactor is bolted to".
    /// </summary>
    /// <remarks>
    ///     The original irradiated the whole map by starting a <c>nuclear_fallout</c> weather event.
    ///     There is no "the map" here, so the failure is delivered in three separate layers:
    ///     a destructive blast that opens the reactor compartment, a non-destructive shockwave
    ///     that throws the crew and everything loose around the rest of the ship, and a storm of
    ///     radioactive slag that stays behind as a lasting hazard.
    /// </remarks>
    public void Meltdown(EntityUid uid, RbmkReactorComponent reactor)
    {
        // Meltdown() is reachable from more than one alert, and the slagged reactor keeps ticking
        // for a moment before it stops. Only the first call gets to detonate.
        if (reactor.Slagged)
            return;

        reactor.Slagged = true;
        reactor.IsRunning = false;
        reactor.AlertActive = false;
        StopAlarm(reactor);
        UpdateVisuals(uid, reactor);

        // A slagged core must not keep feeding the grid: MaxSupply is persistent state, and nothing
        // else will tick this machine again to clear it. Without this the wreck keeps supplying the
        // last tick's wattage forever.
        if (TryComp<PowerSupplierComponent>(uid, out var supplier))
            supplier.MaxSupply = 0f;

        reactor.LastPowerProduced = 0f;

        if (TryComp<RadiationSourceComponent>(uid, out var source))
        {
            source.Intensity = reactor.MeltdownRadiation;
            source.Enabled = true;
        }

        _audio.PlayPvs(reactor.MeltdownSound, uid);
        _shipEvent.EngineAnnounce(uid, Loc.GetString("rbmk-announce-meltdown"));

        // Destructive core. Full intensity out to BlastCoreRadius, tapering to nothing at
        // BlastRadius. Structural damage in the ReactorMeltdown prototype is what actually opens the
        // hull; the plateau is what makes sure the reactor compartment is gone rather than damaged.
        var slope = reactor.BlastPeakIntensity / MathF.Max(reactor.BlastCoreRadius, 0.01f);
        var intensity = _explosion.RadiusToIntensity(reactor.BlastRadius, slope, reactor.BlastPeakIntensity);

        _explosion.QueueExplosion(uid,
            reactor.BlastExplosionType,
            intensity,
            slope,
            reactor.BlastPeakIntensity,
            canCreateVacuum: true);

        // NSV13: empulse(src, 25, 15). SS14 takes light range, energy drain and duration.
        _emp.EmpPulse(_transform.GetMapCoordinates(uid), 15f, 100000f, 30f);

        // The part the crew feels: no damage, just everything not bolted down becoming a projectile.
        _shipEvent.Shockwave(uid,
            _transform.GetMapCoordinates(uid),
            reactor.ShockwaveRadius,
            reactor.ShockwaveSpeed,
            reactor.ShockwaveKnockdown);

        // Lights go out, and the survivors get to sit in the flickering.
        _shipEvent.BlowLights(uid, reactor.LightDestroyChance, reactor.LightFlickerChance);
        _shipEvent.ShakeLocal(uid, 16f, 2f);

        _shipEvent.SpawnSlagStorm(uid, reactor.SlagCount, reactor.SlagPrototype, reactor.SlagThrowSpeed);

        // Dump the gate contents into the room at double core temperature.
        if (_nodeContainer.TryGetNodes(uid,
                reactor.CoolantInputName,
                reactor.ModeratorInputName,
                reactor.CoolantOutputName,
                out PipeNode? coolantIn,
                out PipeNode? moderatorIn,
                out PipeNode? coolantOut))
        {
            var mix = _atmosphere.GetContainingMixture(uid, true, true);

            if (mix != null)
            {
                var hot = CelsiusToKelvin(reactor.Temperature) * 2f;

                foreach (var node in new[] { coolantIn, moderatorIn, coolantOut })
                {
                    node.Air.Temperature = hot;
                    _atmosphere.Merge(mix, node.Air);
                    node.Air.Clear();
                }
            }
        }
    }

    /// <summary>
    ///     Ported from <c>blowout()</c> (rbmk.dm:491-502): a larger explosion followed by a meltdown.
    /// </summary>
    public void Blowout(EntityUid uid, RbmkReactorComponent reactor)
    {
        StopAlarm(reactor);
        _audio.PlayPvs(reactor.ExplosionSound, uid);
        _shipEvent.EngineAnnounce(uid, Loc.GetString("rbmk-announce-blowout"));

        // Original uses GLOB.MAX_EX_* (engine maximum). Here the vessel bursts over half again the
        // meltdown radius, which on a ship is already most of the reactor deck.
        var radius = reactor.BlastRadius * 1.5f;
        var slope = reactor.BlastPeakIntensity / MathF.Max(reactor.BlastCoreRadius, 0.01f);
        var intensity = _explosion.RadiusToIntensity(radius, slope, reactor.BlastPeakIntensity);

        _explosion.QueueExplosion(uid,
            reactor.BlastExplosionType,
            intensity,
            slope,
            reactor.BlastPeakIntensity,
            canCreateVacuum: true);

        Meltdown(uid, reactor);
    }

    /// <summary>
    ///     Ported from <c>start_up()</c> (rbmk.dm:533-544).
    /// </summary>
    public void StartUp(EntityUid uid, RbmkReactorComponent reactor)
    {
        if (reactor.Slagged || reactor.IsRunning)
            return;

        reactor.IsRunning = true;
        reactor.DesiredK = 1f;
        reactor.NextTick = _timing.CurTime;

        // rbmk.dm:537-544 start_up(): random startup sting, and the reactor hum becomes the
        // compartment's looping ambience (the prototype carries the sound, disabled).
        if (reactor.StartupSounds.Count > 0)
            _audio.PlayPvs(_random.Pick(reactor.StartupSounds), uid);

        _ambient.SetAmbience(uid, true);
        UpdateVisuals(uid, reactor);
    }

    /// <summary>
    ///     Ported from <c>shut_down()</c> (rbmk.dm:547-555). Called from the tick when the core has
    ///     gone cold and unfuelled, so it deliberately does not touch the visuals itself - the
    ///     caller updates them at the end of the same tick.
    /// </summary>
    private void ShutDown(EntityUid uid, RbmkReactorComponent reactor)
    {
        reactor.IsRunning = false;
        reactor.K = 0f;
        reactor.DesiredK = 0f;
        reactor.Temperature = 0f;

        // The hum belongs to the running core; silence it when the reaction dies.
        _ambient.SetAmbience(uid, false);
    }

    private static float KelvinToCelsius(float kelvin) => kelvin - KelvinOffset;

    private static float CelsiusToKelvin(float celsius) => celsius + KelvinOffset;
}
