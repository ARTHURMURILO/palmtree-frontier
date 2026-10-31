// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._PS.Ships;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Emp;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.ParticleAccelerator.Components;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Content.Shared.Audio;
using Content.Shared.Radiation.Components;
using Content.Shared.Singularity.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Content.Server.Light.EntitySystems;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Light.Components;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     NSV13 Stormdrive: the fusion reactor. Gas in one pipe, nucleium out the other, and a cubic
///     power curve in between.
/// </summary>
/// <remarks>
///     <para>
///     Ported from <c>stormdrive.dm</c>. The tick is the same 2 s cadence as the RBMK — NSV13's
///     <c>next_slowprocess</c> gate never actually suppresses anything, so "10 seconds" in the
///     original comments is 5 ticks, i.e. 10 s of wall clock, but every per-tick figure means
///     per 2 s.
///     </para>
///     <para>
///     The machine is completed by <see cref="StormdriveRodSystem"/> (the rod bay),
///     <see cref="StormdriveConsoleSystem"/> (the control console), <see cref="StormdriveRepairSystem"/>
///     (the meltdown recovery chain) and <see cref="StormdriveAnomalySystem"/> (the three anomalies an
///     unstable core can tear loose); this class owns the physics only.
///     </para>
///     <para>
///     The danger model is completely different from the RBMK's: <c>control_rod_percent = 0</c> is
///     not idle, it is <em>maximum reactivity with cooling disabled</em>, because the cooling step
///     is gated on <c>control_rod_percent &gt; 0</c>. Pulling the rods out is how you melt it down.
///     </para>
/// </remarks>
public sealed class StormdriveSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly PoweredLightSystem _poweredLight = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    [Dependency] private readonly ShipEventSystem _shipEvent = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;

    /// <summary>Prototype a burnt-out rod is swapped for (<c>/obj/item/control_rod/irradiated</c>).</summary>
    private static readonly EntProtoId SpentRodPrototype = "ControlRodIrradiated";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StormdriveComponent, MapInitEvent>(OnMapInit);
        // Subscribed on the reactor, not on the projectile: SingularityGeneratorSystem already owns
        // the (ParticleProjectileComponent, StartCollideEvent) pair and the event bus allows exactly
        // one subscription per component/event pair - registering ours threw on startup.
        SubscribeLocalEvent<StormdriveComponent, StartCollideEvent>(OnParticleCollide);
    }

    private void OnMapInit(EntityUid uid, StormdriveComponent comp, MapInitEvent args)
    {
        // Every drive is slightly different: each gas gets its own ±5% modifier, rolled once.
        comp.PlasmaModifier = RollModifier(comp);
        comp.ConstrictedPlasmaModifier = RollModifier(comp);
        comp.TritiumModifier = RollModifier(comp);
        comp.OxygenModifier = RollModifier(comp);
        comp.NitrogenModifier = RollModifier(comp);
        comp.CarbonDioxideModifier = RollModifier(comp);
        comp.WaterVaporModifier = RollModifier(comp);
        comp.HyperNobliumModifier = RollModifier(comp);
        comp.NitrousOxideModifier = RollModifier(comp);
        comp.NitrylModifier = RollModifier(comp);
        comp.BZModifier = RollModifier(comp);
        comp.StimulumModifier = RollModifier(comp);
        comp.PluoxiumModifier = RollModifier(comp);
        comp.NucleiumModifier = RollModifier(comp);

        // A mapper (or the pre-started prototype) can load the bay through ContainerFill. Adopt
        // whatever is already in the container so those rods count toward efficiency/integrity
        // instead of being inert decoration.
        if (_container.TryGetContainer(uid, StormdriveComponent.RodContainer, out var bay))
        {
            foreach (var rodUid in bay.ContainedEntities)
            {
                if (!HasComp<ControlRodComponent>(rodUid))
                    continue;

                comp.ControlRods.Add(rodUid);
            }
        }

        // The pre-started prototype skips ignition, which is where the reaction state is normally
        // seeded. Without this the core spawns "running" with zero stability, and the stability
        // roll in handle_reactor_stability() reads it as a core about to tear itself apart: a
        // gravity pull and a thunderclap every few ticks on a perfectly healthy reactor.
        if (comp.State == StormdriveState.Running)
        {
            comp.ReactorStability = 100f;

            if (comp.ReactionRate <= 0f)
                comp.ReactionRate = 5f;
        }

        RecalculateRods(comp);
        UpdateVisuals(uid, comp);
    }

    private float RollModifier(StormdriveComponent comp)
    {
        return _random.NextFloat(1f - comp.GasModifier / 100f, 1f + comp.GasModifier / 100f);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<StormdriveComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextProcess > now)
                continue;

            comp.NextProcess = now + TimeSpan.FromSeconds(comp.ProcessInterval);

            // §C.1 step 1: a reactor past RUNNING (repair and worse) is inert.
            if (comp.State >= StormdriveState.Repair)
                continue;

            SlowProcess(uid, comp);
        }
    }

    /// <summary>
    ///     stormdrive.dm:511-517 - <c>bullet_act(/obj/item/projectile/energy/accelerated_particle)</c>:
    ///     every particle that hits the core dumps its energy into <c>heat</c> and then tries to
    ///     ignite the drive.
    /// </summary>
    /// <remarks>
    ///     This is the only way to start a stormdrive, and it is why the particle accelerator exists
    ///     in the first place. It was missing from the port: the PA fired, the particles sailed
    ///     through the core and nothing happened, portable emitters included.
    /// </remarks>
    private void OnParticleCollide(EntityUid uid, StormdriveComponent comp, ref StartCollideEvent args)
    {
        if (!TryComp<ParticleProjectileComponent>(args.OtherEntity, out var particle))
            return;

        // The DM original absorbs the projectile: it never passes through the core.
        QueueDel(args.OtherEntity);

        comp.Heat += ParticleHeat(particle.State);

        if (comp.State is StormdriveState.Running or StormdriveState.Meltdown or StormdriveState.Maintenance)
            return;

        TryStart(uid, comp);
    }

    /// <summary>
    ///     Heat delivered by one particle, by PA power level. The BYOND original uses
    ///     <c>projectile.energy</c>; SS14 particles only carry a power state, so the levels are
    ///     mapped onto degrees here (a level-3 shot is enough on its own to reach
    ///     <see cref="StormdriveComponent.StartThreshold"/>).
    /// </summary>
    private static float ParticleHeat(ParticleAcceleratorPowerState state)
    {
        return state switch
        {
            ParticleAcceleratorPowerState.Level0 => 3f,
            ParticleAcceleratorPowerState.Level1 => 6f,
            ParticleAcceleratorPowerState.Level2 => 10f,
            ParticleAcceleratorPowerState.Level3 => 20f,
            _ => 0f,
        };
    }

    /// <summary>
    ///     Transcribed from <c>slowprocess()</c> (stormdrive.dm:591-719). Order matters throughout:
    ///     the power figure is computed before this tick's heat is applied, and the meltdown check
    ///     lives inside the icon update at the very end.
    /// </summary>
    private void SlowProcess(EntityUid uid, StormdriveComponent comp)
    {
        // Step 2: a melted-down core just keeps irradiating.
        if (comp.State == StormdriveState.Meltdown)
        {
            if (TryComp<RadiationSourceComponent>(uid, out var dead))
            {
                dead.Intensity = comp.MeltdownRadiation * comp.RadiationModifier;
                dead.Enabled = true;
            }

            return;
        }

        if (!_nodeContainer.TryGetNodes(uid, comp.FuelInputName, comp.WasteOutputName,
                out PipeNode? fuelNode, out PipeNode? wasteNode))
            return;

        var air1 = fuelNode.Air;
        var air2 = wasteNode.Air;

        // Step 3: the armed meltdown countdown.
        if (comp.ReactorEndTimes && HandleDestabilization(uid, comp))
            return;

        // Step 4: player-toggled fuel dump.
        if (comp.DumpingFuel)
            DumpFuel(uid, comp, air1, air2);

        // Step 5: running gate.
        if (comp.State != StormdriveState.Running || comp.Heat <= comp.StartThreshold)
        {
            Deactivate(uid, comp);
            return;
        }

        // Steps 6-9: the reaction.
        var totalMoles = air1.TotalMoles;
        var nucleiumPowerReduction = 0f;
        var fuelCheck = totalMoles <= 0f
            ? 0f
            : (air1.GetMoles(Gas.Plasma) /* + air1.GetMoles(Gas.ConstrictedPlasma) */ + air1.GetMoles(Gas.Tritium))
              / totalMoles * 100f;

        if (totalMoles >= comp.ReactionRate && fuelCheck >= 12.5f && comp.ReactionRate > 0f)
        {
            React(uid, comp, air1, out nucleiumPowerReduction);
            comp.HeatGain = 5f + comp.ReactionRate;
        }
        else
        {
            comp.ReactorStability -= 1f;
            comp.HeatGain = -5f;
        }

        // Steps 10-12: electrical power, from the temperature *before* this tick's heat gain.
        // This one-tick lag is in the original and is kept.
        comp.InputPower = MathF.Pow(comp.Heat / 150f, 3f) * comp.InputPowerModifier;
        comp.LastPowerProduced = MathF.Max(0f,
            comp.BasePower * comp.InputPower - nucleiumPowerReduction);

        // DM calls this late in the tick ("if(last_power_produced > 3000000)"), and the value it
        // reads is the one computed above, so the ordering here is equivalent.
        if (comp.LastPowerProduced > 3e6f)
            HandleOverload(uid, comp);

        // Steps 13-19.
        HandleReactionRate(comp);
        HandleHeat(uid, comp);
        HandleTemperatureReinforcement(comp);
        HandleFuelProduction(comp, air1, air2);
        HandleReactorStability(uid, comp);
        UpdateStatus(uid, comp);
        UpdateVisuals(uid, comp);

        // Step 20: radiation scales with temperature. Rescaled for SS14, where intensity is damage
        // per second rather than a BYOND radiation accumulator - see
        // RbmkReactorComponent.RadiationPerDegree for the reasoning.
        if (TryComp<RadiationSourceComponent>(uid, out var source))
            source.Intensity = Math.Clamp(comp.Heat * comp.RadiationPerDegree * comp.RadiationModifier, 0f, comp.MaxCoreRadiation);

        // Step 21: bleed heat into the room.
        AmbientBleed(uid, comp);

        // Steps 23-25: feed the grid.
        if (TryComp<PowerSupplierComponent>(uid, out var supplier))
            supplier.MaxSupply = comp.LastPowerProduced;
    }

    /// <summary>§C.1 steps 9b-9o: the slug maths. All modifiers are slug-relative.</summary>
    private void React(EntityUid uid, StormdriveComponent comp, GasMixture air1, out float nucleiumPowerReduction)
    {
        var rate = comp.ReactionRate;
        var slug = air1.Remove(rate);

        // BRANCH NOTE (stormdrive branch): the /tg/ + NSV13 reactor gases live on `engines-all`.
        // The drive runs on vanilla plasma, tritium, oxygen, nitrogen, CO2 and water vapour here, and
        // every missing gas contributes 0 to the maths below, exactly as if it had never been there.
        var plasma = slug.GetMoles(Gas.Plasma);
        var constricted = 0f; // slug.GetMoles(Gas.ConstrictedPlasma);
        var tritium = slug.GetMoles(Gas.Tritium);
        var o2 = slug.GetMoles(Gas.Oxygen);
        var n2 = slug.GetMoles(Gas.Nitrogen);
        var co2 = slug.GetMoles(Gas.CarbonDioxide);
        var h2o = slug.GetMoles(Gas.WaterVapor);
        var nob = 0f; // slug.GetMoles(Gas.HyperNoblium);
        var n2o = slug.GetMoles(Gas.NitrousOxide);
        var nitryl = 0f; // slug.GetMoles(Gas.Nitryl);
        var bz = 0f; // slug.GetMoles(Gas.BZ);
        var stim = 0f; // slug.GetMoles(Gas.Stimulum);
        var pluox = 0f; // slug.GetMoles(Gas.Pluoxium);
        var nucleium = 0f; // slug.GetMoles(Gas.Nucleium);

        var rorTotal = plasma * (0.5f * comp.PlasmaModifier)
                       + constricted * (1f * comp.ConstrictedPlasmaModifier)
                       + tritium * (1.5f * comp.TritiumModifier)
                       + n2 * (-0.5f * comp.NitrogenModifier)
                       + h2o * (-0.5f * comp.WaterVaporModifier)
                       + nob * (-1f * comp.HyperNobliumModifier);

        comp.ReactionRateModifier = rorTotal / rate;

        var ipmTotal = rate
                       + tritium * (1.25f * comp.TritiumModifier)
                       + o2 * (1.25f * comp.OxygenModifier)
                       + pluox * (1.25f * comp.PluoxiumModifier)
                       + stim * (2.5f * comp.StimulumModifier)
                       - plasma * (0.85f * comp.PlasmaModifier)
                       - co2 * (0.5f * comp.CarbonDioxideModifier)
                       - nob * (0.5f * comp.HyperNobliumModifier);

        comp.InputPowerModifier = ipmTotal / rate;

        var coolingTotal = rate
                           + nob * (4f * comp.HyperNobliumModifier)
                           + nitryl * (2.025f * comp.NitrylModifier)
                           + n2 * (2.025f * comp.NitrogenModifier)
                           + co2 * (2.025f * comp.CarbonDioxideModifier)
                           - tritium * (0.75f * comp.TritiumModifier)
                           - nucleium * (0.75f * comp.NucleiumModifier)
                           - stim * (0.75f * comp.StimulumModifier);

        comp.CoolingPowerModifier = coolingTotal / rate;

        var radiationTotal = rate
                             + tritium * (10f * comp.TritiumModifier)
                             + plasma * (5f * comp.PlasmaModifier)
                             + nucleium * (5f * comp.NucleiumModifier)
                             + bz * (5f * comp.BZModifier)
                             - stim * (0.75f * comp.StimulumModifier)
                             - nitryl * (0.75f * comp.NitrylModifier);

        comp.RadiationModifier = radiationTotal / rate;

        // Note: this one is keyed on NITROUS (n2o), not nitryl. The original reuses the
        // `no2_modifier` *name* for nitryl elsewhere, which is why this looks inconsistent.
        var reinforcementTotal = rate
                                 + pluox * (1.5f * comp.PluoxiumModifier)
                                 + tritium * (1.25f * comp.TritiumModifier)
                                 + n2o * (1.25f * comp.NitrousOxideModifier)
                                 - nucleium * (0.75f * comp.NucleiumModifier)
                                 - stim * (0.75f * comp.StimulumModifier)
                                 - bz * (0.75f * comp.BZModifier);

        comp.TemperatureModifier = reinforcementTotal / rate;

        var degradationTotal = rate
                               + n2o * (0.75f * comp.NitrousOxideModifier)
                               + nob * (0.5f * comp.HyperNobliumModifier)
                               + pluox * (0.75f * comp.PluoxiumModifier);

        comp.ControlRodDegradationModifier = degradationTotal / rate;

        nucleiumPowerReduction = nucleium * 1000f;

        // The slug has been destroyed, exactly like the original: no gas is converted, nucleium is
        // created from nothing later in HandleFuelProduction. A Stormdrive is a matter sink.

        if (air1.TotalMoles > rate * 12f + 20f)
            comp.ReactorStability -= 0.51f;

        if (air1.TotalMoles > 0f &&
            (air1.GetMoles(Gas.Plasma) /* + air1.GetMoles(Gas.ConstrictedPlasma) */ + air1.GetMoles(Gas.Tritium))
            / air1.TotalMoles * 100f >= 25f)
        {
            if (comp.ReactorStability < 100f)
                comp.ReactorStability += 0.5f;
            else
                comp.ReactorStability -= 0.01f;
        }
        else
        {
            comp.ReactorStability -= 0.01f;
        }

    }

    /// <summary>§C.2 <c>handle_reaction_rate()</c>: a first-order lag, always half the gap.</summary>
    private static void HandleReactionRate(StormdriveComponent comp)
    {
        var rods = (100f - comp.ControlRodPercent) * comp.ControlRodModifier;
        var target = 0.5f + 1e-3f * rods * rods * comp.ReactionRateModifier
                     + 1e-5f * comp.Heat * comp.Heat;

        comp.TargetReactionRate = target;
        comp.ReactionRate += (target - comp.ReactionRate) / 2f;
    }

    /// <summary>
    ///     §C.2 <c>handle_heat()</c>.
    /// </summary>
    /// <remarks>
    ///     The cooling step is gated on <c>ControlRodPercent &gt; 0</c>. That single condition is the
    ///     whole danger of the machine: rods fully withdrawn means maximum reactivity <em>and</em> no
    ///     cooling at all.
    /// </remarks>
    private void HandleHeat(EntityUid uid, StormdriveComponent comp)
    {
        comp.Heat += comp.HeatGain;

        comp.TargetHeat = -1f + MathF.Pow(2f,
            0.1f * (100f - comp.ControlRodPercent) * comp.ControlRodModifier);

        var cooling = comp.CoolingPower * comp.CoolingPowerModifier;

        if (comp.Heat > comp.TargetHeat + (cooling - comp.HeatGain))
        {
            if (CanCool(uid, comp) && comp.ControlRodPercent > 0f)
                comp.Heat -= cooling;
        }
    }

    /// <summary>
    ///     §C.2 <c>handle_temperature_reinforcement()</c>: the four thresholds drift halfway toward
    ///     <c>initial * modifier</c> every tick.
    /// </summary>
    private static void HandleTemperatureReinforcement(StormdriveComponent comp)
    {
        var m = comp.TemperatureModifier;

        comp.TemperatureNominal += (200f * m - comp.TemperatureNominal) / 2f;
        comp.TemperatureHot += (400f * m - comp.TemperatureHot) / 2f;
        comp.TemperatureCritical += (650f * m - comp.TemperatureCritical) / 2f;
        comp.TemperatureMeltdown += (800f * m - comp.TemperatureMeltdown) / 2f;
    }

    /// <summary>
    ///     §C.2 <c>handle_ftl_fuel_production()</c>: nucleium is created, never converted. If the
    ///     waste line is backed up it forces its way back into the fuel line instead.
    /// </summary>
    private void HandleFuelProduction(StormdriveComponent comp, GasMixture air1, GasMixture air2)
    {
        if (comp.Heat <= 400f || comp.ReactionRate <= 0f)
            return;

        // BRANCH NOTE (stormdrive branch): this whole step exists to manufacture nucleium. With the
        // reactor gases on `engines-all`, the drive runs the same heat cycle and simply produces no
        // exotic waste, so the waste line stays gas the drive can re-burn instead of a byproduct.
        var heatKelvin = comp.Heat + 273.15f;
        // var fuelAmount = air1.GetMoles(Gas.Plasma)
        //                  + air1.GetMoles(Gas.ConstrictedPlasma)
        //                  + air1.GetMoles(Gas.Tritium);

        if (air2.Pressure >= comp.MaxOutputPressure)
        {
            // air1.AdjustMoles(Gas.Nucleium, fuelAmount / comp.ReactionRate / 10f * comp.InputPowerModifier);
            air1.Temperature = heatKelvin;
        }
        else
        {
            // air2.AdjustMoles(Gas.Nucleium, comp.ReactionRate / 10f * comp.InputPowerModifier);
            air2.Temperature = heatKelvin;
        }
    }

    /// <summary>
    ///     §C.2 <c>can_cool()</c>. Wears the rods and is called from the status update as well as
    ///     from the heat step, so rod wear is charged twice per tick — that is faithful, and it is
    ///     why the rod slider ages them.
    /// </summary>
    /// <remarks>
    ///     Each mounted rod wears independently, exactly like the DM loop: a 0.8 chance per tick,
    ///     scaled by <see cref="StormdriveComponent.InputPower"/>,
    ///     <see cref="StormdriveComponent.ControlRodDegradationModifier"/> and the insertion depth.
    ///     A rod that reaches zero becomes an irradiated one, so a dead rod never silently starts
    ///     working again.
    /// </remarks>
    private bool CanCool(EntityUid uid, StormdriveComponent comp)
    {
        // Snapshot: ReplaceSpentRod rewrites an entry in the list while this loop is running.
        foreach (var rodUid in comp.ControlRods.ToArray())
        {
            if (!TryComp<ControlRodComponent>(rodUid, out var rod))
                continue;

            // DM checks for a failed rod *before* applying this tick's wear, and never replaces the
            // irradiated rod it swapped in (that type is explicitly skipped by the original). The
            // check has to come first and be terminal, otherwise a dead rod is respawned every tick.
            if (rod.Spent)
            {
                ReplaceSpentRod(uid, comp, rodUid);
                continue;
            }

            if (_random.Prob(0.8f * comp.ControlRodDegradationModifier))
            {
                rod.Integrity -= comp.InputPower / 75000f
                                 * comp.ControlRodDegradationModifier
                                 * comp.ControlRodPercent;
            }
        }

        RecalculateRods(comp);

        if (comp.ControlRodIntegrity <= 0f)
        {
            if (comp.State == StormdriveState.Running)
                _shipEvent.TryEngineAnnounce(uid, Loc.GetString("stormdrive-announce-rods-failed"), ref comp.NextAlert);

            return false;
        }

        if (comp.ControlRodIntegrity <= 35f && comp.WarningState <= StormdriveWarning.None &&
            comp.State == StormdriveState.Running)
        {
            _shipEvent.TryEngineAnnounce(uid,
                Loc.GetString("stormdrive-announce-rods-worn", ("integrity", MathF.Round(comp.ControlRodIntegrity))),
                ref comp.NextAlert);
        }

        return comp.ControlRodIntegrity > 0f;
    }

    /// <summary>
    ///     §C.2 <c>handle_control_rod_efficiency()</c> + <c>handle_control_rod_integrity()</c>:
    ///     the core only ever sees the mean of what is mounted. With no rods the modifier is 1 (so
    ///     the target-temperature curve behaves) but the integrity is 0, which disables cooling.
    /// </summary>
    public void RecalculateRods(StormdriveComponent comp)
    {
        if (comp.ControlRods.Count == 0)
        {
            comp.ControlRodModifier = 1f;
            comp.ControlRodIntegrity = 0f;
            return;
        }

        var effectiveness = 0f;
        var integrity = 0f;

        foreach (var rodUid in comp.ControlRods)
        {
            if (!TryComp<ControlRodComponent>(rodUid, out var rod))
                continue;

            effectiveness += rod.Effectiveness;
            integrity += rod.Integrity;
        }

        comp.ControlRodModifier = effectiveness / comp.ControlRods.Count;
        comp.ControlRodIntegrity = integrity / comp.ControlRods.Count;
    }

    /// <summary>
    ///     Swap a burnt-out rod for the irradiated version, in place inside the core. The DM version
    ///     deleted the rod and instantiated <c>/obj/item/control_rod/irradiated</c> into the
    ///     machine's contents; the position in the list is preserved so a UI selection stays sane.
    /// </summary>
    private void ReplaceSpentRod(EntityUid uid, StormdriveComponent comp, EntityUid rodUid)
    {
        var index = comp.ControlRods.IndexOf(rodUid);
        if (index < 0)
            return;

        if (!TryComp<ControlRodComponent>(rodUid, out var rod) || !rod.Replaceable)
            return;

        var container = _container.EnsureContainer<Container>(uid, StormdriveComponent.RodContainer);
        var spent = Spawn(SpentRodPrototype, Transform(uid).Coordinates);

        comp.ControlRods[index] = spent;
        _container.Insert(spent, container);
        QueueDel(rodUid);
    }

    /// <summary>
    ///     §<c>handle_overload()</c>: a drive running above 3 MW starts throwing its weight around.
    /// </summary>
    /// <remarks>
    ///     The DM version picks a light radius and a severity from the power band. The grounding-rod
    ///     tesla_zap branch has no equivalent here (there are no grounding rods in this fork), so
    ///     above 8 MW an overload instead dents reactor stability - which is what the zap's side
    ///     effect was.
    /// </remarks>
    private void HandleOverload(EntityUid uid, StormdriveComponent comp)
    {
        var power = comp.LastPowerProduced;

        // DM wrote these as BYOND prob() percentages: prob(0.1) is a tenth of a percent, not ten
        // percent. Keep the literals as probabilities (0.001f = 0.1 %) so the bands match.
        if (power >= 3e6f && power < 5e6f)
        {
            if (_random.Prob(0.001f))
                FlickerLights(uid, 8f, 0.25f);
            return;
        }

        if (power >= 5e6f && power < 8e6f)
        {
            if (_random.Prob(0.001f))
                FlickerLights(uid, 25f, 0.25f);

            if (_random.Prob(0.01f))
                FlickerLights(uid, 8f, 0.25f);
            return;
        }

        if (power >= 8e6f && power < 1.2e7f)
        {
            if (_random.Prob(0.001f))
                FlickerLights(uid, 0f, 0.25f);

            if (_random.Prob(0.01f))
            {
                BreakLights(uid, 12f, 3);
                comp.ReactorStability -= _random.Next(5, 11);
            }
            return;
        }

        if (power < 1.2e7f)
            return;

        if (_random.Prob(0.01f))
            FlickerLights(uid, 0f, 0.5f);

        if (_random.Prob(0.05f))
        {
            BreakLights(uid, 12f, 5);
            comp.ReactorStability -= _random.Next(10, 21);
        }
    }

    /// <summary>
    ///     Flicker every light within <paramref name="range"/> of the drive (0 = the whole ship),
    ///     each with <paramref name="chance"/>. DM did the same to <c>orange(range, src)</c> or to
    ///     every light sharing the overmap.
    /// </summary>
    /// <remarks>
    ///     Ship-scoped on purpose: a range of zero means "every light aboard", not "every light in
    ///     the world". Without the grid check an overload on one vessel would strobe every other
    ///     ship and trade post in the same map.
    /// </remarks>
    private void FlickerLights(EntityUid uid, float range, float chance)
    {
        if (!_shipEvent.TryGetGrid(uid, out var grid))
            return;

        var origin = _transform.GetWorldPosition(uid);
        var lights = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();

        while (lights.MoveNext(out var lightUid, out var light, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            if (range > 0f && (_transform.GetWorldPosition(xform) - origin).Length() > range)
                continue;

            if (!_random.Prob(chance))
                continue;

            _poweredLight.ToggleBlinkingLight(lightUid, light, true);
            lightUid.SpawnTimer(TimeSpan.FromSeconds(_random.NextFloat(8f, 14f)), () =>
            {
                if (!Deleted(lightUid) && TryComp<PoweredLightComponent>(lightUid, out var lit))
                    _poweredLight.ToggleBlinkingLight(lightUid, lit, false);
            });
        }
    }

    /// <summary>Burn out or shatter a handful of nearby light fixtures on the drive's own ship.</summary>
    private void BreakLights(EntityUid uid, float range, int count)
    {
        if (!_shipEvent.TryGetGrid(uid, out var grid))
            return;

        var origin = _transform.GetWorldPosition(uid);
        var candidates = new List<EntityUid>();

        var lights = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (lights.MoveNext(out var lightUid, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            if ((_transform.GetWorldPosition(xform) - origin).Length() > range)
                continue;

            candidates.Add(lightUid);
        }

        for (var i = 0; i < count && candidates.Count > 0; i++)
        {
            var pick = _random.PickAndTake(candidates);
            if (TryComp<PoweredLightComponent>(pick, out var light))
                _poweredLight.TryDestroyBulb(pick, light);
        }
    }

    /// <summary>§C.2 <c>handle_reactor_stability()</c>, including the anomaly branch.</summary>
    private void HandleReactorStability(EntityUid uid, StormdriveComponent comp)
    {
        comp.ReactorStability = Math.Clamp(comp.ReactorStability, 0f, 100f);

        // §grav_pull(): a localised gravity event that yanks everything toward the core and puts
        // anyone standing in range on the floor. This is a warning shot, not an attack.
        if (_random.Prob((100f - comp.ReactorStability) / 3f / 100f))
        {
            // DM plays /sound/effects/empulse.ogg; this fork has no such file, so use the sound the
            // engine's own EMP effect uses instead of logging a missing resource every pulse.
            _audio.PlayPvs("/Audio/Effects/Lightning/lightningbolt.ogg", uid);

            // DM grav_pull() is a single `step_towards` per object plus a knockdown - a lurch, not
            // a well. Strength 1.5 for two seconds keeps that feel.
            _shipEvent.GravityPull(uid, comp.Heat / 40f + 1f, 1.5f, TimeSpan.FromSeconds(2));
        }

        if (comp.ReactorStability < 75f)
        {
            if (_random.Prob((100f - comp.ReactorStability) / 4f / 100f))
            {
                if (_random.Prob(0.5f))
                    comp.Heat += comp.ReactionRate * _random.Next(5, 9);
                else
                    comp.ReactionRate += comp.ReactionRate / _random.Next(3, 6);
            }
        }

        // §handle_reactor_stability(): below 15 % stability the core starts tearing anomalies into
        // realspace. The DM roll is weird - it ranges 1..10 and only 9 and 10 make a squall - so the
        // weighting is 40 / 40 / 20 for surge / sheer / squall.
        if (comp.ReactorStability < 15f && _random.Prob(0.01f))
        {
            var roll = _random.Next(1, 11);
            var proto = roll switch
            {
                <= 4 => comp.SurgeAnomaly,
                <= 8 => comp.SheerAnomaly,
                _ => comp.SquallAnomaly,
            };

            Spawn(proto, _transform.GetMapCoordinates(uid));
            comp.ReactorStability += 15f;
            _audio.PlayPvs("/Audio/Effects/Lightning/lightningbolt.ogg", uid);
        }

        // Entropy nudge. First match wins, exactly like the DM range switch.
        if (comp.ReactorStability < 1f)
            comp.Heat += 1f;
        else if (comp.ReactorStability < 15f)
            comp.Heat += 0.5f;
        else if (comp.ReactorStability < 30f)
            comp.Heat += 0.1f;
        else if (comp.ReactorStability < 75f)
            comp.Heat += 0.01f;
    }

    /// <summary>§C.2 <c>ambient_temp_bleed()</c>.</summary>
    private void AmbientBleed(EntityUid uid, StormdriveComponent comp)
    {
        var mix = _atmosphere.GetContainingMixture(uid, true, true);

        if (mix == null)
            return;

        var heatKelvin = comp.Heat + 273.15f;

        if (mix.Temperature <= heatKelvin)
            mix.Temperature += (heatKelvin - mix.Temperature) / 2f;
    }

    /// <summary>
    ///     §C.3 <c>update_icon()</c>, with the icon part dropped and the side effects kept.
    /// </summary>
    /// <remarks>
    ///     In the original this function is not purely cosmetic: it wears the control rods (via
    ///     <c>can_cool()</c>) and it is where the overheat warning and the meltdown trigger live.
    ///     Keeping those here preserves the difficulty curve, so do not "clean this up".
    /// </remarks>
    public void UpdateStatus(EntityUid uid, StormdriveComponent comp)
    {
        if (comp.State is StormdriveState.Meltdown or >= StormdriveState.Repair)
            return;

        // Side effect: rod wear. Called here as well as in HandleHeat, exactly as the original does.
        CanCool(uid, comp);

        if (comp.State != StormdriveState.Running)
            return;

        if (comp.Heat > comp.TemperatureCritical && comp.WarningState < StormdriveWarning.Overheat)
        {
            comp.WarningState = StormdriveWarning.Overheat;
            _shipEvent.TryEngineAnnounce(uid, Loc.GetString("stormdrive-announce-overheat"), ref comp.NextAlert, urgent: true);
            StopOverheatLoop(comp);
            // Looping, not a one-shot sting: the DM original relays this to the ship's overmap as a
            // continuous alarm, and a single second of klaxon while the core keeps climbing reads
            // as a bug rather than as a warning.
            comp.AlarmStream = _audio.PlayPvs(comp.AlarmSound, uid, AudioParams.Default.WithLoop(true))?.Entity;
            comp.OverheatStream = _audio.PlayPvs(comp.OverheatLoopSound, uid, AudioParams.Default.WithLoop(true))?.Entity;
        }
        else if (comp.Heat <= comp.TemperatureCritical && comp.WarningState == StormdriveWarning.Overheat)
        {
            // §C.2 check_meltdown_warning(): the core came back down. The DM original announced
            // that (with override) because "it stopped" is very different information from silence.
            comp.WarningState = StormdriveWarning.None;
            StopOverheatLoop(comp);
            _shipEvent.TryEngineAnnounce(uid, Loc.GetString("stormdrive-announce-meltdown-averted"),
                ref comp.NextAlert, urgent: true);
        }

        // Strictly greater, and checked at the end of the tick like the original.
        if (comp.Heat > comp.TemperatureMeltdown)
            StartMeltdown(uid, comp);
    }

    /// <summary>
    ///     §<c>handle_reactor_destabilization()</c>: shatter windows within six tiles, shake the deck
    ///     and knock over anyone within five tiles.
    /// </summary>
    private void Destabilize(EntityUid uid)
    {
        var origin = _transform.GetMapCoordinates(uid);
        var damageable = new DamageSpecifier { DamageDict = { ["Structural"] = 1 } };

        foreach (var target in _lookup.GetEntitiesInRange(origin.MapId, origin.Position, 6f))
        {
            if (target == uid)
                continue;

            if (!_tag.HasTag(target, WindowTag))
                continue;

            if (!TryComp<DamageableComponent>(target, out var damage))
                continue;

            damageable.DamageDict["Structural"] = _random.Next(50, 201);
            _damageable.TryChangeDamage(target, damageable, damageable: damage);
        }

        _shipEvent.ShakeLocal(uid, 10f, 1f);
        _shipEvent.GravityPull(uid, 5f, 1.5f, TimeSpan.FromSeconds(2));
    }

    /// <summary>Silence the looping klaxon and overheat bed, if they are running.</summary>
    private void StopOverheatLoop(StormdriveComponent comp)
    {
        if (comp.AlarmStream is { } alarm)
        {
            comp.AlarmStream = null;

            if (Exists(alarm))
                _audio.Stop(alarm);
        }

        if (comp.OverheatStream is not { } stream)
            return;

        comp.OverheatStream = null;

        if (Exists(stream))
            _audio.Stop(stream);
    }

    /// <summary>
    ///     The icon half of §C.3 <c>update_icon()</c>, routed through Appearance so the client can
    ///     play the ported RSI animations.
    /// </summary>
    /// <remarks>
    ///     Every branch is a straight copy of the DM temperature bands, including the one the
    ///     original itself annotated "NEED NEW ICON": above <c>reactor_hot</c> and below critical,
    ///     the sprite keeps the hot state. The rod overlay only shows while the rods can cool, and
    ///     its band is the insertion depth in quarters.
    /// </remarks>
    public void UpdateVisuals(EntityUid uid, StormdriveComponent comp)
    {
        if (!HasComp<AppearanceComponent>(uid))
            return;

        var state = comp.State switch
        {
            StormdriveState.Maintenance => StormdriveVisualState.Maintenance,
            StormdriveState.Meltdown => StormdriveVisualState.Meltdown,
            StormdriveState.Repair => StormdriveVisualState.Repair,
            StormdriveState.Reinforce => StormdriveVisualState.Reinforce,
            StormdriveState.Refit => StormdriveVisualState.Refit,
            StormdriveState.Running => InternalHeatState(comp),
            _ => StormdriveVisualState.Off,
        };

        _appearance.SetData(uid, StormdriveVisuals.Core, state);

        if (comp.ControlRods.Count == 0 || comp.ControlRodIntegrity <= 0f)
        {
            _appearance.SetData(uid, StormdriveVisuals.Rods, "none");
            return;
        }

        // round(control_rod_percent): 0-24 -> 1, 25-49 -> 2, 50-74 -> 3, 75-100 -> 4.
        var band = Math.Clamp((int) MathF.Round(comp.ControlRodPercent) / 25 + 1, 1, 4);
        var count = Math.Clamp(comp.ControlRods.Count, 0, 5);

        _appearance.SetData(uid, StormdriveVisuals.Rods, $"rods_{count}_{band}");
    }

    /// <summary>The running-core temperature bands from <c>update_icon()</c>.</summary>
    private static StormdriveVisualState InternalHeatState(StormdriveComponent comp)
    {
        if (comp.Heat <= 0f)
            return StormdriveVisualState.Off;

        if (comp.Heat <= comp.TemperatureNominal)
            return StormdriveVisualState.On;

        if (comp.Heat <= comp.TemperatureHot)
            return StormdriveVisualState.Hot;

        // DM: "icon_state = reactor_hot //NEED NEW ICON".
        if (comp.Heat <= comp.TemperatureCritical)
            return StormdriveVisualState.Hot;

        return StormdriveVisualState.Overheat;
    }

    /// <summary>
    ///     §E3 <c>start_meltdown()</c>: arm the countdown. The rods fail first, and the reactor is
    ///     given 18 seconds of warning before it actually goes.
    /// </summary>
    public void StartMeltdown(EntityUid uid, StormdriveComponent comp)
    {
        if (comp.WarningState >= StormdriveWarning.Meltdown)
            return;

        // DM start_meltdown() burns through every mounted rod: each one is zeroed and replaced by
        // an irradiated rod. That is not cosmetic - it takes cooling offline for the whole fuse, so
        // the meltdown that was just announced actually lands. Without it the crew could cool the
        // core below the threshold during the eighteen seconds and the sequence would fizzle while
        // the "CONTROL RODS HAVE FAILED" alarm was still ringing.
        foreach (var rodUid in comp.ControlRods.ToArray())
        {
            if (!TryComp<ControlRodComponent>(rodUid, out var rod))
                continue;

            rod.Integrity = 0f;
            ReplaceSpentRod(uid, comp, rodUid);
        }

        RecalculateRods(comp);
        comp.ControlRodIntegrity = 0f;

        _shipEvent.TryEngineAnnounce(uid,
            Loc.GetString("stormdrive-announce-meltdown-armed",
                ("seconds", (int) comp.MeltdownDelay.TotalSeconds)),
            ref comp.NextAlert, urgent: true);

        comp.WarningState = StormdriveWarning.Meltdown;
        comp.MeltdownAt = _timing.CurTime + comp.MeltdownDelay;
        comp.MeltdownCountdownIndex = 0;
        comp.ReactorEndTimes = true;

        _audio.PlayPvs(comp.MeltdownSound, uid);
    }

    /// <summary>
    ///     Count the armed meltdown down out loud, on the same Shortband the rest of the alarms use.
    /// </summary>
    /// <remarks>
    ///     The DM original announced "the rods have failed" and then went quiet for the whole fuse, so
    ///     the only sign of the deadline was a number on the console. A fuse nobody can hear is a
    ///     fuse that surprises people; the marks in <see cref="StormdriveComponent.MeltdownCountdownStages"/>
    ///     are spaced to the reactor's own two-second tick.
    /// </remarks>
    private void AnnounceMeltdownCountdown(EntityUid uid, StormdriveComponent comp)
    {
        var stages = comp.MeltdownCountdownStages;
        var remaining = (int) Math.Ceiling((comp.MeltdownAt - _timing.CurTime).TotalSeconds);

        while (comp.MeltdownCountdownIndex < stages.Length
               && remaining <= stages[comp.MeltdownCountdownIndex])
        {
            var stage = stages[comp.MeltdownCountdownIndex++];
            _shipEvent.EngineAnnounce(uid, Loc.GetString("stormdrive-announce-countdown", ("seconds", stage)));
        }
    }

    /// <summary>
    ///     §C.1 step 3 / §E4: the armed meltdown fires, or fizzles if the core cooled down in time.
    /// </summary>
    /// <returns>True if the reactor should stop processing this tick.</returns>
    private bool HandleDestabilization(EntityUid uid, StormdriveComponent comp)
    {
        if (_timing.CurTime < comp.MeltdownAt)
        {
            AnnounceMeltdownCountdown(uid, comp);

            // §handle_reactor_destabilization(): while the fuse burns, the core keeps shaking the
            // compartment apart. 40 % per tick, windows first, then anyone standing nearby.
            if (_random.Prob(0.4f))
                Destabilize(uid);

            return false;
        }

        if (comp.Heat >= comp.TemperatureMeltdown)
        {
            Meltdown(uid, comp);
            return true;
        }

        // Cooled below the threshold in the eighteen seconds you were given. Reprieve.
        comp.WarningState = StormdriveWarning.None;
        comp.ReactorEndTimes = false;
        return false;
    }

    /// <summary>§E4 <c>meltdown()</c>.</summary>
    public void Meltdown(EntityUid uid, StormdriveComponent comp)
    {
        comp.State = StormdriveState.Meltdown;
        comp.ReactorEndTimes = false;
        comp.ReactionRate = 0f;
        comp.LastPowerProduced = 0f;

        // Nothing will tick this machine again, so the power supplier has to be cleared here or the
        // wreck keeps feeding the grid its final wattage forever.
        if (TryComp<PowerSupplierComponent>(uid, out var supplier))
            supplier.MaxSupply = 0f;

        StopOverheatLoop(comp);
        _audio.PlayPvs(comp.ExplosionSound, uid);
        _shipEvent.TryEngineAnnounce(uid, Loc.GetString("stormdrive-announce-meltdown"), ref comp.NextAlert, urgent: true);

        DoMeltdownEffects(uid, comp);
        UpdateVisuals(uid, comp);

        // DM: flick("meltdown") then, one second later, icon_state = "broken". The flick is the RSI
        // animation on the meltdown state; the timer swaps to the wreckage underneath it.
        uid.SpawnTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!Deleted(uid) && TryComp<StormdriveComponent>(uid, out var drive))
            {
                drive.State = StormdriveState.Meltdown;
                _appearance.SetData(uid, StormdriveVisuals.Core, StormdriveVisualState.Broken);
            }
        });
    }

    /// <summary>
    ///     §E4 <c>do_meltdown_effects()</c>.
    /// </summary>
    /// <remarks>
    ///     Note the asymmetry with the RBMK: the Stormdrive's blast is <em>tiny</em> deliberately
    ///     (<c>explosion(0, 0, 10, 20)</c> — no devastation, no heavy range). The damage comes from
    ///     the EMP, the radiation, the 5 000 K gas cloud, the ship-wide knockdown, and the corium.
    /// </remarks>
    private void DoMeltdownEffects(EntityUid uid, StormdriveComponent comp)
    {
        var coords = _transform.GetMapCoordinates(uid);

        var slope = comp.BlastPeakIntensity / MathF.Max(comp.BlastCoreRadius, 0.01f);
        var intensity = _explosion.RadiusToIntensity(comp.BlastRadius, slope, comp.BlastPeakIntensity);

        _explosion.QueueExplosion(uid,
            comp.BlastExplosionType,
            intensity,
            slope,
            comp.BlastPeakIntensity,
            canCreateVacuum: true);

        // empulse(src, 25, 50): this is the real payload.
        _emp.EmpPulse(coords, comp.EmpRange, 100000f, 60f);

        if (TryComp<RadiationSourceComponent>(uid, out var source))
        {
            source.Intensity = comp.MeltdownRadiation;
            source.Enabled = true;
        }

        // atmos_spawn_air("o2=750;plasma=200;nucleium=100;TEMP=5000")
        var mix = _atmosphere.GetContainingMixture(uid, true, true);
        if (mix != null)
        {
            mix.AdjustMoles(Gas.Oxygen, 750f);
            mix.AdjustMoles(Gas.Plasma, 200f);
            // BRANCH NOTE (stormdrive branch): nucleium is a reactor gas.
            // mix.AdjustMoles(Gas.Nucleium, 100f);
            mix.Temperature = 5000f;
        }

        // Ship-wide knockdown for everything standing.
        _shipEvent.Shockwave(uid, coords, 22f, 7f, TimeSpan.FromSeconds(5));
        _shipEvent.ShakeLocal(uid, 10f, 1.5f);

        // NOTE: no slag storm here. stormdrive.dm's meltdown() only detonates the map's existing
        // nuclear_waste_spawner landmarks - it never throws corium around. That is the RBMK's
        // failure mode, and stacking both on one explosion just made the drive read as an RBMK.
    }

    /// <summary>§D.1 <c>try_start()</c>: needs both fuel equivalence and pre-heat.</summary>
    /// <returns>True if the reactor ignited.</returns>
    public bool TryStart(EntityUid uid, StormdriveComponent comp)
    {
        if (comp.State is StormdriveState.Running or StormdriveState.Meltdown or StormdriveState.Maintenance)
            return false;

        if (!_nodeContainer.TryGetNode(uid, comp.FuelInputName, out PipeNode? fuelNode))
            return false;

        var air1 = fuelNode.Air;

        var ignitionFuel = air1.GetMoles(Gas.Plasma) * 0.5f
                           // + air1.GetMoles(Gas.ConstrictedPlasma) * 1f
                           + air1.GetMoles(Gas.Tritium) * 1.5f
                           - air1.GetMoles(Gas.CarbonDioxide) * 0.5f
                           - air1.GetMoles(Gas.WaterVapor) * 0.5f
                           // - air1.GetMoles(Gas.HyperNoblium) * 1f
                           ;

        if (ignitionFuel < comp.StartThreshold || comp.Heat < comp.StartThreshold)
            return false;

        comp.Heat = comp.StartThreshold + 10f; // 30 °C, hard reset of any pre-heat
        comp.State = StormdriveState.Running;
        comp.ReactorStability = 100f;

        if (comp.ReactionRate <= 0f)
            comp.ReactionRate = 5f;

        comp.WarningState = StormdriveWarning.None;

        // stormdrive.dm:537-542 - the startup sting is picked at random, and the core starts to
        // glow an ominous blue. The prototype carries the hum; it becomes audible only now.
        if (comp.StartupSounds.Count > 0)
            _audio.PlayPvs(_random.Pick(comp.StartupSounds), uid);

        _ambient.SetAmbience(uid, true);
        UpdateVisuals(uid, comp);

        // DM: send_alert("Fission reaction initiated. Reactor now on-line.", override=TRUE)
        _shipEvent.TryEngineAnnounce(uid, Loc.GetString("stormdrive-announce-online"), ref comp.NextAlert, urgent: true);

        return true;
    }

    /// <summary>
    ///     Control-rod setpoint from a console. DM's <c>ui_act</c> assigned <c>control_rod_percent</c>
    ///     and then called <c>update_icon()</c>, which is not cosmetic: it wears the rods and runs
    ///     the overheat check. Both side effects are reproduced here.
    /// </summary>
    public void SetControlRods(EntityUid uid, StormdriveComponent comp, float percent)
    {
        // Math.Clamp lets NaN through; NaN would propagate into target_heat via MathF.Pow and freeze
        // the whole simulation in a comparison-false state.
        if (!float.IsFinite(percent))
            return;

        comp.ControlRodPercent = Math.Clamp(percent, 0f, 100f);
        UpdateStatus(uid, comp);
    }

    /// <summary>
    ///     §C.4 <c>engage_maintenance()</c> / <c>disengage_maintenance()</c>. Maintenance is the only
    ///     state in which the lid may be opened, and it can only be entered from idle.
    /// </summary>
    /// <returns>False when the transition was refused.</returns>
    public bool SetMaintenance(EntityUid uid, StormdriveComponent comp, bool enabled)
    {
        if (enabled)
        {
            if (comp.State != StormdriveState.Idle)
                return false;

            Deactivate(uid, comp);
            comp.State = StormdriveState.Maintenance;
        }
        else
        {
            if (comp.State != StormdriveState.Maintenance)
                return false;

            comp.State = StormdriveState.Idle;
        }

        UpdateVisuals(uid, comp);
        return true;
    }

    /// <summary>§C.4 <c>deactivate()</c>. Also used by the repair chain's factory reset, which is why
    /// it is public rather than private.</summary>
    public void Deactivate(EntityUid uid, StormdriveComponent comp)
    {
        if (comp.State == StormdriveState.Running)
            _shipEvent.TryEngineAnnounce(uid, Loc.GetString("stormdrive-announce-offline"), ref comp.NextAlert);

        // "protect our warmup": IDLE keeps the particle-accelerator pre-heat.
        if (comp.State != StormdriveState.Idle)
            comp.Heat = 0f;

        comp.LastPowerProduced = 0f;
        comp.ReactionRate = 0f;
        comp.ReactorStability = 0f;

        if (comp.State != StormdriveState.Maintenance)
            comp.State = StormdriveState.Idle;

        if (TryComp<PowerSupplierComponent>(uid, out var supplier))
            supplier.MaxSupply = 0f;

        // The DM leaves the icon to the next update_icon() call, which only runs from slowprocess().
        // slowprocess() bails out here ("running gate"), so the sprite used to stay on the last
        // running frame forever: a shut-down drive still looked like it was lit.
        _ambient.SetAmbience(uid, false);
        UpdateVisuals(uid, comp);
    }

    /// <summary>§C.1 step 4: dump fuel into the waste line.</summary>
    private void DumpFuel(EntityUid uid, StormdriveComponent comp, GasMixture air1, GasMixture air2)
    {
        if (air2.Pressure >= comp.MaxOutputPressure)
        {
            _shipEvent.TryEngineAnnounce(uid, Loc.GetString("stormdrive-announce-dump-blocked"), ref comp.NextAlert);
            comp.DumpingFuel = false;
            return;
        }

        var removed = air1.Remove(MathF.Min(25f, air1.TotalMoles));

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            air2.AdjustMoles(i, removed.GetMoles(i));

        air2.Temperature = comp.Heat + 273.15f;
    }
}
