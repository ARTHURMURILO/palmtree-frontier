// SPDX-FileCopyrightText: 2025 VMSolidus <evilexecutive@gmail.com>
// SPDX-FileCopyrightText: 2025 corresp0nd <46357632+corresp0nd@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Shared.Atmos;
using Robust.Shared.Configuration;

namespace Content.Shared._EE.CCVars;

[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed partial class ECCVars
{
    /// <summary>
    ///     The cutoff on power properly doing damage, pulling stuff around, and shooting stronger lightning.
    ///     The supermatter will also spawn anomalies.
    /// </summary>
    public static readonly CVarDef<float> SupermatterPowerPenaltyThreshold =
        CVarDef.Create("supermatter.power_penalty_threshold", 5000f, CVar.SERVER);

    /// <summary>
    ///     Above this, the supermatter spawns anomalies at an increased rate, and gains +1 bolt of electricity.
    /// </summary>
    public static readonly CVarDef<float> SupermatterSeverePowerPenaltyThreshold =
        CVarDef.Create("supermatter.power_penalty_threshold_severe", 7000f, CVar.SERVER);

    /// <summary>
    ///     Above this, the supermatter spawns pyro anomalies at an increased rate, and gains +1 bolt of electricity.
    /// </summary>
    public static readonly CVarDef<float> SupermatterCriticalPowerPenaltyThreshold =
        CVarDef.Create("supermatter.power_penalty_threshold_critical", 9000f, CVar.SERVER);

    /// <summary>
    ///     The minimum pressure for a pure ammonia (miasma analog in this fork) atmosphere
    ///     to begin being consumed.
    /// </summary>
    public static readonly CVarDef<float> SupermatterAmmoniaConsumptionPressure =
        CVarDef.Create("supermatter.ammonia_consumption_pressure", Atmospherics.OneAtmosphere * 0.01f, CVar.SERVER);

    /// <summary>
    ///     How the amount of ammonia consumed per tick scales with partial pressure.
    /// </summary>
    public static readonly CVarDef<float> SupermatterAmmoniaPressureScaling =
        CVarDef.Create("supermatter.ammonia_pressure_scaling", Atmospherics.OneAtmosphere * 0.05f, CVar.SERVER);

    /// <summary>
    ///     How much the amount of ammonia consumed per tick scales with the gas mix power ratio.
    /// </summary>
    public static readonly CVarDef<float> SupermatterAmmoniaGasMixScaling =
        CVarDef.Create("supermatter.ammonia_gas_mix_scaling", 0.3f, CVar.SERVER);

    /// <summary>
    ///     The amount of matter power generated for every mole of ammonia consumed.
    /// </summary>
    public static readonly CVarDef<float> SupermatterAmmoniaPowerGain =
        CVarDef.Create("supermatter.ammonia_power_gain", 10f, CVar.SERVER);

    /// <summary>
    ///     Maximum safe operational temperature in degrees Celsius.
    ///     Supermatter begins taking damage above this temperature.
    /// </summary>
    public static readonly CVarDef<float> SupermatterHeatPenaltyThreshold =
        CVarDef.Create("supermatter.heat_penalty_threshold", 40f, CVar.SERVER | CVar.REPLICATED); // Triad: replicated so the console's client-side base figures match server config overrides

    /// <summary>
    ///     The percentage of the supermatter's matter power that is converted into power each atmos tick.
    /// </summary>
    public static readonly CVarDef<float> SupermatterMatterPowerConversion =
        CVarDef.Create("supermatter.matter_power_conversion", 10f, CVar.SERVER);

    /// <summary>
    ///     Divisor on the amount of damage that the supermatter takes from absorbing hot gas.
    /// </summary>
    public static readonly CVarDef<float> SupermatterMoleHeatPenalty =
        CVarDef.Create("supermatter.mole_heat_penalty", 350f, CVar.SERVER);

    /// <summary>
    ///     Above this threshold the supermatter takes damage from gas moles.
    ///     Below this threshold, the supermatter can heal damage.
    /// </summary>
    public static readonly CVarDef<float> SupermatterMolePenaltyThreshold =
        CVarDef.Create("supermatter.mole_penalty_threshold", 1800f, CVar.SERVER);

    /// <summary>
    ///     Divisor on the amount of oxygen released during atmospheric reactions.
    /// </summary>
    public static readonly CVarDef<float> SupermatterOxygenReleaseModifier =
        CVarDef.Create("supermatter.oxygen_release_modifier", 325f, CVar.SERVER);

    /// <summary>
    ///     Divisor on the amount of plasma released during atmospheric reactions.
    /// </summary>
    public static readonly CVarDef<float> SupermatterPlasmaReleaseModifier =
        CVarDef.Create("supermatter.plasma_release_modifier", 750f, CVar.SERVER);

    /// <summary>
    ///     Percentage of inhibitor gas needed before the charge inertia chain reaction effect starts.
    /// </summary>
    public static readonly CVarDef<float> SupermatterPowerlossInhibitionGasThreshold =
        CVarDef.Create("supermatter.powerloss_inhibition_gas_threshold", 0.2f, CVar.SERVER);

    /// <summary>
    ///     Moles of the gas needed before the charge inertia chain reaction effect starts.
    ///     Scales powerloss inhibition down until this amount of moles is reached.
    /// </summary>
    public static readonly CVarDef<float> SupermatterPowerlossInhibitionMoleThreshold =
        CVarDef.Create("supermatter.powerloss_inhibition_mole_threshold", 20f, CVar.SERVER);

    /// <summary>
    ///     Bonus powerloss inhibition boost if this amount of moles is reached.
    /// </summary>
    public static readonly CVarDef<float> SupermatterPowerlossInhibitionMoleBoostThreshold =
        CVarDef.Create("supermatter.powerloss_inhibition_mole_boost_threshold", 500f, CVar.SERVER);

    /// <summary>
    ///     Base amount of radiation the supermatter emits. Radiation is a hazard here,
    ///     not a power path: supermatter radiation never charges radiation collectors.
    /// </summary>
    public static readonly CVarDef<float> SupermatterRadsBase =
        CVarDef.Create("supermatter.rads_base", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Directly multiplies the radiation put out by the supermatter. Be careful with this.
    /// </summary>
    public static readonly CVarDef<float> SupermatterRadsModifier =
        CVarDef.Create("supermatter.rads_modifier", 1f, CVar.SERVER);

    /// <summary>
    ///     Optional hard ceiling on the supermatter's radiation intensity. Zero disables the cap.
    /// </summary>
    public static readonly CVarDef<float> SupermatterMaxRadiation =
        CVarDef.Create("supermatter.max_radiation", 0f, CVar.SERVER);

    /// <summary>
    ///     Maximum distance, in tiles, that the crystal will arc lightning to a tesla coil.
    ///     The arc range still scales with internal energy, up to this cap.
    /// </summary>
    public static readonly CVarDef<float> SupermatterLightningRange =
        CVarDef.Create("supermatter.lightning_range", 7f, CVar.SERVER);

    /// <summary>
    ///     Internal energy required per additional tesla coil the crystal can keep charged.
    ///     One coil is always supported; every 4000 energy above that adds another.
    /// </summary>
    public static readonly CVarDef<float> SupermatterLightningPowerPerCoil =
        CVarDef.Create("supermatter.lightning_power_per_coil", 4000f, CVar.SERVER);

    /// <summary>
    ///     A tesla coil is only re-struck once its battery has fallen below this fraction of
    ///     its maximum charge. This throttles arcs to the coil's actual discharge rate.
    /// </summary>
    public static readonly CVarDef<float> SupermatterLightningCoilRechargeThreshold =
        CVarDef.Create("supermatter.lightning_coil_recharge_threshold", 0.25f, CVar.SERVER);

    /// <summary>
    ///     Base radius, in tiles, of a supermatter delamination explosion.
    /// </summary>
    public static readonly CVarDef<float> SupermatterDelamBaseRadius =
        CVarDef.Create("supermatter.delam_base_radius", 20f, CVar.SERVER);

    /// <summary>
    ///     Internal energy per additional tile of delamination blast radius. The radius grows
    ///     by one tile for every this many points of internal energy at the moment of failure.
    /// </summary>
    public static readonly CVarDef<float> SupermatterDelamPowerPerRadius =
        CVarDef.Create("supermatter.delam_power_per_radius", 4000f, CVar.SERVER);

    /// <summary>
    ///     Optional hard ceiling on delamination blast radius, in tiles. Zero disables the cap.
    /// </summary>
    public static readonly CVarDef<float> SupermatterDelamMaxRadius =
        CVarDef.Create("supermatter.delam_max_radius", 0f, CVar.SERVER);

    /// <summary>
    ///     Maximum number of hyperflux anomalies spawned by a delamination. The actual count
    ///     ramps from one to this value with the crystal's internal energy.
    /// </summary>
    public static readonly CVarDef<int> SupermatterDelamAnomalyMax =
        CVarDef.Create("supermatter.delam_anomaly_max", 6, CVar.SERVER);

    /// <summary>
    ///     Internal energy at which a delamination spawns the maximum number of anomalies.
    /// </summary>
    public static readonly CVarDef<float> SupermatterDelamAnomalyPower =
        CVarDef.Create("supermatter.delam_anomaly_power", 20000f, CVar.SERVER);

    /// <summary>
    ///     Multiplier on the overall power produced during supermatter atmospheric reactions.
    /// </summary>
    public static readonly CVarDef<float> SupermatterReactionPowerModifier =
        CVarDef.Create("supermatter.reaction_power_modifier", 0.55f, CVar.SERVER);

    /// <summary>
    ///     Divisor on the amount that atmospheric reactions increase the supermatter's temperature.
    /// </summary>
    public static readonly CVarDef<float> SupermatterThermalReleaseModifier =
        CVarDef.Create("supermatter.thermal_release_modifier", 5f, CVar.SERVER);

    /// <summary>
    ///     How often the supermatter should announce its status.
    /// </summary>
    public static readonly CVarDef<float> SupermatterYellTimer =
        CVarDef.Create("supermatter.yell_timer", 120f, CVar.SERVER);
}
