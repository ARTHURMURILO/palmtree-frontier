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
    ///     The minimum pressure for a pure ammonia atmosphere to begin being consumed.
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
    ///     Base amount of radiation that the supermatter emits.
    /// </summary>
    public static readonly CVarDef<float> SupermatterRadsBase =
        CVarDef.Create("supermatter.rads_base", 2f, CVar.SERVER | CVar.REPLICATED); //Frontier: 3<2. Still potentially too high. Rads can reach 60+. // Triad: replicated so the console's client-side base radiation matches server config overrides

    /// <summary>
    ///     Directly multiplies the amount of rads put out by the supermatter. Be SUPER careful with this.
    /// </summary>
    public static readonly CVarDef<float> SupermatterRadsModifier =
        CVarDef.Create("supermatter.rads_modifier", 1f, CVar.SERVER);

    /// <summary>
    ///     Optional hard ceiling on the supermatter's radiation intensity. Zero disables the
    ///     ceiling, which is the default: dangerous setups scale with the Coyote curve.
    /// </summary>
    public static readonly CVarDef<float> SupermatterMaxRadiation =
        CVarDef.Create("supermatter.max_radiation", 0f, CVar.SERVER);

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
