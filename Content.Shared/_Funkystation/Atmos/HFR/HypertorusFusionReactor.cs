// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus <90893484+LaCumbiaDelCoronavirus@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 marc-pelletier <113944176+marc-pelletier@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Robust.Shared.Serialization;
using Content.Shared.Atmos;

namespace Content.Shared._Funkystation.Atmos.HFR
{
    /// <summary>
    /// Class to store fusion reactor constants.
    /// </summary>
    public static class HypertorusFusionReactor
    {
        #region Fusion-Specific Constants

        /// <summary>
        /// Maximum instability before the reaction goes endothermic.
        /// </summary>
        public const float FusionInstabilityEndothermality = 4f;

        /// <summary>
        /// Gas fusion power values for each gas type.
        /// </summary>
        public static float GasFusionPower(Gas gas)
        {
            return gas switch
            {
                Gas.Hydrogen => 1.0f,
                Gas.Tritium => 1.5f,
                Gas.Plasma => 0.8f,
                Gas.Oxygen => 0.3f,
                Gas.Nitrogen => 0.4f,
                Gas.CarbonDioxide => 0.5f,
                Gas.NitrousOxide => 0.6f,
                Gas.WaterVapor => 0.2f,
                Gas.Frezon => 1.2f,
                Gas.BZ => 0.7f,
                Gas.ProtoNitrate => 0.9f,
                Gas.Nitrium => 0.5f,
                Gas.Zauker => 1.3f,
                Gas.Healium => 0.4f,
                Gas.Halon => 0.3f,
                Gas.Pluoxium => 0.6f,
                Gas.AntiNoblium => 2.0f,
                Gas.HyperNoblium => 0.1f,
                Gas.Helium => 0.2f,
                _ => 0.0f // Default for unlisted gases
            };
        }

        #endregion

        #region Physical Constants

        /// <summary>
        /// Speed of light, in m/s.
        /// </summary>
        public const float LightSpeed = 299792458f;

        /// <summary>
        /// Calculation between the Planck constant and the lambda of the lightwave.
        /// </summary>
        public const float PlanckLightConstant = 2e-16f;

        /// <summary>
        /// Radius of H2 calculated based on the amount of atoms in a mole (with additions for balancing).
        /// </summary>
        public const float CalculatedH2Radius = 120e-4f;

        /// <summary>
        /// Radius of tritium calculated based on the amount of atoms in a mole (with additions for balancing).
        /// </summary>
        public const float CalculatedTritRadius = 230e-3f;

        /// <summary>
        /// Power conduction in the void, used to calculate the efficiency of the reaction.
        /// </summary>
        public const float VoidConduction = 1e-2f;

        #endregion

        #region Fusion Reaction

        /// <summary>
        /// Mole count required (tritium/hydrogen) to start a fusion reaction.
        /// </summary>
        public const float FusionMoleThreshold = 25f;

        /// <summary>
        /// Used to reduce the gas power to a more useful amount.
        /// </summary>
        public const float InstabilityGasPowerFactor = 0.003f;

        /// <summary>
        /// Used to calculate the toroidal size for the instability.
        /// </summary>
        public const float ToroidVolumeBreakeven = 1000f;

        #endregion

        #region Heat Conduction

        /// <summary>
        /// Conduction of heat inside the fusion reactor.
        /// </summary>
        public const float MetallicVoidConductivity = 0.38f;

        /// <summary>
        /// Conduction of heat near the external cooling loop.
        /// </summary>
        public const float HighEfficiencyConductivity = 0.975f;

        #endregion

        #region Temperature Scale

        // Palmtree: Funky authored the fusion math for a 25,000,000 K reactor ceiling, which needed a
        // fork-wide 1000x raise of Atmospherics.Tmax to work. The fork keeps the upstream 262,144 K
        // atmos ceiling instead, so the HFR runs at the fork's atmos temperature scale: one Funky
        // fusion degree is TemperatureScale kelvin here. Every absolute temperature constant in this
        // file (and every kelvin-per-second heat rate that is proportional to one) is multiplied by
        // this factor so the reactor behaves like Funky's, just at ~95x lower temperatures.
        // Keep FusionMaximumTemperature at or below Atmospherics.Tmax or the atmos sim clamps the mix.

        /// <summary>
        /// Fusion temperature ceiling (K). Funky used 25,000,000 K; this is the old atmos ceiling.
        /// </summary>
        public const float FusionMaximumTemperature = Atmospherics.Tmax;

        /// <summary>
        /// How many kelvin one Funky fusion degree is worth (262144 / 25000000, ~0.01049).
        /// </summary>
        public const float TemperatureScale = FusionMaximumTemperature / 25000000f;

        /// <summary>
        /// Fusion temperature at which each power level ends (index 0 is the level 0/1 boundary).
        /// Level 0 is ambient air up to 500 K, matching Funky. The remaining boundaries are geometric
        /// from 500 K to 40% of the ceiling (Funky's 1e7 K / 2.5e7 K ratio) because ambient air is
        /// still ~293 K here, so Funky's decade steps cannot fit under a 262,144 K ceiling.
        /// The client console graph uses the same table.
        /// </summary>
        public static readonly float[] PowerLevelUpperTemperatures = BuildPowerLevelUpperTemperatures();

        private static float[] BuildPowerLevelUpperTemperatures()
        {
            const float first = 500f;
            // Funky reaches level 6 at 1e7 K, i.e. 40% of its 2.5e7 K ceiling. Keep that ratio.
            var last = 1e7f * TemperatureScale;
            var step = MathF.Pow(last / first, 1f / 5f);

            var boundaries = new float[6];
            for (var i = 0; i < boundaries.Length; i++)
                boundaries[i] = first * MathF.Pow(step, i);

            return boundaries;
        }

        /// <summary>
        /// Gets the fusion power level for a fusion mix temperature (see
        /// <see cref="PowerLevelUpperTemperatures"/>).
        /// </summary>
        public static int GetPowerLevel(float fusionTemperature)
        {
            // A broken temperature must not look like a level 6 reaction.
            if (!float.IsFinite(fusionTemperature))
                return 0;

            for (var i = 0; i < PowerLevelUpperTemperatures.Length; i++)
            {
                if (fusionTemperature <= PowerLevelUpperTemperatures[i])
                    return i;
            }

            return 6;
        }

        /// <summary>
        /// Parses a gas id out of a fusion recipe. Recipes are authored in YAML, so this uses
        /// <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> instead of a throwing parse: a
        /// malformed recipe must never throw inside the atmos tick (see the prototype test too).
        /// </summary>
        public static bool TryParseGas(string? gasId, out Gas gas)
        {
            return Enum.TryParse(gasId, out gas);
        }

        /// <summary>
        /// Parses every valid gas id out of a recipe gas list, skipping ids that do not name a gas.
        /// </summary>
        public static IEnumerable<Gas> ParseGases(IEnumerable<string> gasIds)
        {
            foreach (var gasId in gasIds)
            {
                if (TryParseGas(gasId, out var gas))
                    yield return gas;
            }
        }

        /// <summary>
        /// Parses a gas id, falling back to <paramref name="fallback"/> when the id is invalid.
        /// </summary>
        public static Gas ParseGasOr(string? gasId, Gas fallback)
        {
            return TryParseGas(gasId, out var gas) ? gas : fallback;
        }

        /// <summary>
        /// Internal fusion mix volume in liters. The reactor always uses this volume; the old console
        /// "cooling volume" control changed the coolant port mix volume in tg, which does not map to
        /// SS14's shared pipe-net mixture, so the control was removed instead of being wired up here.
        /// </summary>
        public const float InternalFusionVolume = 5000f;

        /// <summary>
        /// Internal moderator mix volume in liters.
        /// </summary>
        public const float ModeratorInternalVolume = 10000f;

        #endregion

        #region Power and Damage

        /// <summary>
        /// Sets the multiplier for the damage.
        /// </summary>
        public const float DamageCapMultiplier = 0.005f;

        #endregion

        #region Iron Accumulation

        /// <summary>
        /// Chance in percentage points per fusion level of iron accumulation when operating at unsafe levels.
        /// </summary>
        public const float IronChancePerFusionLevel = 17f;

        /// <summary>
        /// Amount of iron accumulated per second whenever we fail our saving throw.
        /// </summary>
        public const float IronAccumulatedPerSecond = 0.005f;

        /// <summary>
        /// Maximum amount of iron that can be healed per second.
        /// Calculated to mostly keep up with fusion level 5.
        /// </summary>
        public const float IronOxygenHealPerSecond = IronAccumulatedPerSecond * (100f - IronChancePerFusionLevel) / 100f;

        /// <summary>
        /// Amount of oxygen in moles required to fully remove 100% iron content.
        /// Currently about 2409 mol. Calculated to consume at most 10 mol/s.
        /// </summary>
        public const float OxygenMolesConsumedPerIronHeal = 10f / IronOxygenHealPerSecond;

        #endregion

        #region Integrity Alarms

        /// <summary>
        /// If integrity percent remaining is less than this value, the monitor sets off the melting alarm.
        /// </summary>
        public const float HypertorusMeltingPercent = 5f;

        /// <summary>
        /// If integrity percent remaining is less than this value, the monitor sets off the emergency alarm.
        /// </summary>
        public const float HypertorusEmergencyPercent = 25f;

        /// <summary>
        /// If integrity percent remaining is less than this value, the monitor sets off the danger alarm.
        /// </summary>
        public const float HypertorusDangerPercent = 50f;

        /// <summary>
        /// If integrity percent remaining is less than this value, the monitor sets off the warning alarm.
        /// </summary>
        public const float HypertorusWarningPercent = 100f;

        #endregion

        #region Timing

        /// <summary>
        /// Warning time delay in seconds.
        /// </summary>
        public const float WarningTimeDelay = 60f;

        /// <summary>
        /// Minimum cooldown to prevent accent sounds from layering (in seconds).
        /// </summary>
        public const float HypertorusAccentSoundMinCooldown = 3f;

        /// <summary>
        /// Countdown time for the hypertorus (in seconds).
        /// </summary>
        public const float HypertorusCountdownTime = 91f; // 1 second buffer for announcements

        #endregion

        #region Overfull Damage
        // Damage source: Too much mass in the fusion mix at high fusion levels
        // Currently, this is 2700 moles at 1 Kelvin, linearly scaling down to a maximum of 1800 safe moles at max fusion temp

        /// <summary>
        /// Start taking overfull damage at this power level.
        /// </summary>
        public const float HypertorusOverfullMinPowerLevel = 6f;

        /// <summary>
        /// Take 0 damage beneath this much fusion mass at 1 degree Kelvin.
        /// </summary>
        public const float HypertorusOverfullMaxSafeColdFusionMoles = 2700f;

        /// <summary>
        /// Take 0 damage beneath this much fusion mass at maximum fusion temperature.
        /// </summary>
        public const float HypertorusOverfullMaxSafeHotFusionMoles = 1800f;

        /// <summary>
        /// Every 200 moles, 1 point of damage per second.
        /// </summary>
        public const float HypertorusOverfullMolarSlope = 1f / 200f;

        /// <summary>
        /// Derived temperature slope from the molar slope.
        /// </summary>
        public const float HypertorusOverfullTemperatureSlope = HypertorusOverfullMolarSlope * 
            (HypertorusOverfullMaxSafeColdFusionMoles - HypertorusOverfullMaxSafeHotFusionMoles) / 
            (FusionMaximumTemperature - 1f);

        /// <summary>
        /// Derived constant to set damage = 0 at desired thresholds.
        /// </summary>
        public const float HypertorusOverfullConstant = -(HypertorusOverfullMolarSlope * 
            HypertorusOverfullMaxSafeHotFusionMoles + HypertorusOverfullTemperatureSlope * FusionMaximumTemperature);

        #endregion

        #region Subcritical Healing
        // Heal source: Small enough mass in the fusion mix

        /// <summary>
        /// Start healing when fusion mass is below this threshold.
        /// </summary>
        public const float HypertorusSubcriticalMoles = 1200f;

        /// <summary>
        /// Heal one point per second per this many moles under the threshold.
        /// </summary>
        public const float HypertorusSubcriticalScale = 400f;

        #endregion

        #region Cold Coolant Healing
        // Heal source: Cold enough coolant

        /// <summary>
        /// Heal up to this many points of damage per second at 1 degree Kelvin.
        /// </summary>
        public const float HypertorusColdCoolantMaxRestore = 2.5f;

        /// <summary>
        /// Start healing below this temperature (100,000 K in Funky's temperature scale).
        /// </summary>
        public const float HypertorusColdCoolantThreshold = 100000f * TemperatureScale;

        /// <summary>
        /// Derived scale for cold coolant healing.
        /// </summary>
        public static readonly float HypertorusColdCoolantScale = HypertorusColdCoolantMaxRestore / (float)Math.Log10(HypertorusColdCoolantThreshold);

        #endregion

        #region Iron Content Damage
        // Damage source: Iron content

        /// <summary>
        /// Start taking damage over this threshold, up to a maximum of (1 - MaxSafeIron) per tick at 100% iron.
        /// </summary>
        public const float HypertorusMaxSafeIron = 0.35f;

        #endregion

        #region Hypercritical Damage
        // Damage source: Extreme levels of mass in fusion mix at any power level
        // Note: Ignores the damage cap!

        /// <summary>
        /// Start taking damage over this threshold.
        /// </summary>
        public const float HypertorusHypercriticalMoles = 10000f;

        /// <summary>
        /// Take this much damage per mole over the threshold per second.
        /// </summary>
        public const float HypertorusHypercriticalScale = 0.002f;

        /// <summary>
        /// Take at most this much damage per second.
        /// </summary>
        public const float HypertorusHypercriticalMaxDamage = 20f;

        #endregion

        #region Moderator Spillage

        /// <summary>
        /// Weak spill rate for hypercritical moderator.
        /// </summary>
        public const float HypertorusWeakSpillRate = 0.0005f;

        /// <summary>
        /// Weak spill chance for hypercritical moderator.
        /// </summary>
        public const float HypertorusWeakSpillChance = 1f;

        /// <summary>
        /// Start spilling superhot moderator gas when over this pressure threshold.
        /// </summary>
        public const float HypertorusMediumSpillPressure = 10000f;

        /// <summary>
        /// Initial amount to spill for medium spill.
        /// </summary>
        public const float HypertorusMediumSpillInitial = 0.25f;

        /// <summary>
        /// Amount of moderator mix to spill per second until mended for medium spill.
        /// </summary>
        public const float HypertorusMediumSpillRate = 0.01f;

        /// <summary>
        /// Pressure threshold for strong spill of moderator gas.
        /// </summary>
        public const float HypertorusStrongSpillPressure = 12000f;

        /// <summary>
        /// Initial amount to spill for strong spill.
        /// </summary>
        public const float HypertorusStrongSpillInitial = 0.75f;

        /// <summary>
        /// Amount of moderator mix to spill per second until mended for strong spill.
        /// </summary>
        public const float HypertorusStrongSpillRate = 0.05f;

        #endregion
    }

    [Flags]
    public enum HypertorusFlags
    {
        None = 0,
        BaseExplosion = 1,
        MediumExplosion = 2,
        DevastatingExplosion = 4,
        RadiationPulse = 8,
        EMP = 16,
        MinimumSpread = 32,
        MediumSpread = 64,
        BigSpread = 128,
        MassiveSpread = 256,
        CriticalMeltdown = 512
    }

    [Flags]
    public enum HypertorusStatusFlags
    {
        None = 0,
        Emped = 1,
        HighPowerDamage = 2,
        IronContentDamage = 4,
        HighFuelMixMole = 8,
        IronContentIncrease = 16
    }
}