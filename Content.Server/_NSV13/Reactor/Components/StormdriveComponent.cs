// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Atmos;
using Content.Shared.Explosion;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     NSV13 "Class IV Nuclear Storm Drive" — a fusion reactor that injects magnetically
///     constricted plasma into a fission core.
/// </summary>
/// <remarks>
///     Ported from BeeStation/NSV13 <c>nsv13/code/modules/power/stormdrive.dm</c> (1 920 lines).
///     Unlike the RBMK this is a <b>binary</b> atmospherics machine: gas goes in one pipe and the
///     waste (nucleium) comes out the other.
/// </remarks>
[RegisterComponent]
public sealed partial class StormdriveComponent : Component
{
    #region Plumbing

    /// <summary>Pipe node carrying the fuel mixture in.</summary>
    [DataField]
    public string FuelInputName = "fuel";

    /// <summary>Pipe node carrying nucleium waste out.</summary>
    [DataField]
    public string WasteOutputName = "waste";

    #endregion

    #region Core physics

    /// <summary>Core temperature in degrees Celsius. The single most important number.</summary>
    [DataField]
    public float Heat;

    /// <summary>Heat added per tick: 5 + reaction rate while reacting, -5 when starved.</summary>
    [DataField]
    public float HeatGain = 5f;

    /// <summary>The cubic power term. Despite the name this is not the output.</summary>
    [DataField]
    public float InputPower;

    /// <summary>Per-tick gas multiplier on the cubic term.</summary>
    [DataField]
    public float InputPowerModifier = 1f;

    /// <summary>Watts produced last tick.</summary>
    [DataField]
    public float LastPowerProduced;

    [DataField]
    public float BasePower = 67500f;

    /// <summary>UI ceiling only. The curve reaches this at 909 °C.</summary>
    [DataField]
    public float TheoreticalMaximumPower = 15000000f;

    /// <summary>Moles of mixture consumed per tick. Both the throttle and the burn rate.</summary>
    [DataField]
    public float ReactionRate;

    /// <summary>
    ///     Fraction of <see cref="ReactionRate"/> actually drawn from the fuel line each tick.
    /// </summary>
    /// <remarks>
    ///     Palmtree: 0.5. The DM drive was written for stations with gas miners on tap; a ship buys
    ///     its plasma by the canister, so the same reaction now costs half the fuel. The slug is
    ///     scaled back up before the gas modifiers are computed, so the reaction itself is
    ///     unchanged - only the pipe drains at half speed. Electrical output is multiplied by
    ///     this same factor, so the watts per mole stay at the vanilla figure.
    /// </remarks>
    [DataField]
    public float FuelConsumptionFactor = 0.5f;

    [DataField]
    public float TargetReactionRate;

    [DataField]
    public float ReactionRateModifier = 1f;

    #endregion

    #region Control rods

    /// <summary>Rod insertion 0…100. Player-set only; the simulation never moves it.</summary>
    [DataField]
    public float ControlRodPercent;

    /// <summary>Mean effectiveness of the mounted rods.</summary>
    [DataField]
    public float ControlRodModifier = 1f;

    /// <summary>Mean rod integrity, 0…100.</summary>
    [DataField]
    public float ControlRodIntegrity;

    [DataField]
    public float ControlRodDegradationModifier = 1f;

    /// <summary>Maximum number of control rods the core can hold (<c>MAX_CONTROL_RODS</c>).</summary>
    [DataField]
    public int ControlRodCount = 5;

    /// <summary>
    ///     The mounted <see cref="ControlRodComponent"/> entities. They live inside
    ///     <see cref="RodContainer"/> while installed, exactly like the DM version kept them in the
    ///     machine's contents.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> ControlRods = new();

    /// <summary>Container the mounted rods are stored in.</summary>
    public const string RodContainer = "stormdrive_rods";

    /// <summary>Short-lived notice for the rod bay window.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public string RodNotice = string.Empty;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan RodNoticeUntil;

    #endregion

    #region Cooling

    [DataField]
    public float TargetHeat = 200f;

    [DataField]
    public float CoolingPower = 10f;

    [DataField]
    public float CoolingPowerModifier = 1f;

    #endregion

    #region Thresholds

    /// <summary>
    ///     The four temperature thresholds drift toward <c>initial * reactor_temperature_modifier</c>
    ///     by half the remaining distance every tick, so gas composition slowly reshapes them.
    /// </summary>
    [DataField]
    public float TemperatureNominal = 200f;

    [DataField]
    public float TemperatureHot = 400f;

    [DataField]
    public float TemperatureCritical = 650f;

    [DataField]
    public float TemperatureMeltdown = 800f;

    [DataField]
    public float TemperatureModifier = 1f;

    #endregion

    #region Stability

    /// <summary>0…100. Drives gravity pulses, heat spikes, anomalies and eventual meltdown.</summary>
    [DataField]
    public float ReactorStability = 100f;

    /// <summary>Ignition requirement and the "too cold to run" threshold.</summary>
    [DataField]
    public float StartThreshold = 20f;

    #endregion

    #region Gas modifiers

    /// <summary>Percentage spread applied to every per-gas modifier at spawn (±5).</summary>
    [DataField]
    public float GasModifier = 5f;

    public float ConstrictedPlasmaModifier = 1f;
    public float PlasmaModifier = 1f;
    public float TritiumModifier = 1f;
    public float OxygenModifier = 1f;
    public float NitrogenModifier = 1f;
    public float CarbonDioxideModifier = 1f;
    public float WaterVaporModifier = 1f;
    public float HyperNobliumModifier = 1f;
    public float NitrousOxideModifier = 1f;
    public float NitrylModifier = 1f;
    public float BZModifier = 1f;
    public float StimulumModifier = 1f;
    public float PluoxiumModifier = 1f;
    public float NucleiumModifier = 1f;
    public float RadiationModifier = 1f;

    #endregion

    #region State

    /// <summary>
    ///     Link tag for a <see cref="StormdriveConsoleComponent"/>. Plays the role of the DM
    ///     console's <c>reactor_id</c>.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string? ReactorId;

    [DataField]
    public StormdriveState State = StormdriveState.Idle;

    /// <summary>
    ///     Seconds between simulation ticks. NSV13's <c>SSmachines.wait</c> is 2 s; the constant exists
    ///     so tests and tuning can compress the clock without touching the physics.
    /// </summary>
    [DataField]
    public float ProcessInterval = 2f;

    /// <summary>Meltdown is counting down.</summary>
    [DataField]
    public bool ReactorEndTimes;

    /// <summary>Player-toggled fuel dump to the waste line.</summary>
    [DataField]
    public bool DumpingFuel;

    [DataField]
    public StormdriveWarning WarningState = StormdriveWarning.None;

    /// <summary>When the armed meltdown fires.</summary>
    public TimeSpan MeltdownAt;

    /// <summary>
    ///     Earliest time the next routine alarm may be broadcast. Faithful to the DM original's
    ///     <c>can_alert</c>/<c>alert_cooldown</c> pair, which existed because the tick loop would
    ///     otherwise repeat a warning every two seconds.
    /// </summary>
    public TimeSpan NextAlert;

    /// <summary>Handle for the looping klaxon, so it can be stopped when the core recovers.</summary>
    [ViewVariables]
    public EntityUid? AlarmStream;

    /// <summary>Handle for the looping overheat alarm, so it can be stopped when the core recovers.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? OverheatStream;

    /// <summary>When the next simulation tick is due. Zero means "as soon as the system updates".</summary>
    public TimeSpan NextProcess;

    [DataField]
    public TimeSpan MeltdownDelay = TimeSpan.FromSeconds(18);

    /// <summary>
    ///     Seconds-remaining marks at which the armed meltdown counts itself down over the radio.
    /// </summary>
    /// <remarks>
    ///     Ten, eight and six are two apart because the drive itself only ticks every two seconds,
    ///     and a mark per tick is as fast as the early fuse needs to talk. From six down the marks
    ///     are one second apart and are announced by a dedicated one-second timer rather than the
    ///     process tick, so the last seconds of the fuse are heard every second. Descending; each
    ///     mark fires once per fuse.
    /// </remarks>
    [DataField]
    public int[] MeltdownCountdownStages = { 10, 8, 6, 5, 4, 3, 2, 1 };

    /// <summary>How far through <see cref="MeltdownCountdownStages"/> the current fuse is.</summary>
    public int MeltdownCountdownIndex;

    /// <summary>When the next per-second countdown mark is due. Set when the fuse arms.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextCountdownAnnounce;

    /// <summary>Maximum pressure the waste line can take before the dump aborts.</summary>
    [DataField]
    public float MaxOutputPressure = 4500f;

    #endregion

    #region Events

    /// <summary>
    ///     Startup sting, picked at random - stormdrive.dm:539 <c>pick(startup.ogg, startup2.ogg)</c>.
    /// </summary>
    [DataField]
    public List<SoundSpecifier> StartupSounds = new()
    {
        new SoundPathSpecifier("/Audio/_NSV13/Reactor/startup.ogg"),
        new SoundPathSpecifier("/Audio/_NSV13/Reactor/startup2.ogg"),
    };

    [DataField]
    public SoundSpecifier AlarmSound = new SoundPathSpecifier("/Audio/_NSV13/Reactor/alarm.ogg");

    [DataField]
    // stormdrive.dm `start_meltdown()` relays `nsv13/sound/effects/ship/reactor/meltdown.ogg` - a
    // distinct 20 s file from the RBMK's own `rbmk/meltdown.ogg`. The port only shipped the RBMK
    // one, so the fuse was playing the wrong machine's hiss; this is the original drive sting.
    // `core_overheating.ogg` belongs to the warning loop (OverheatLoopSound below).
    public SoundSpecifier MeltdownSound = new SoundPathSpecifier("/Audio/_NSV13/Reactor/stormdrive_meltdown.ogg");

    /// <summary>
    ///     Looping warning bed played while the core is over its critical temperature. The DM
    ///     original relayed exactly this file on the ship's reactor-alert channel, looped, until the
    ///     core came back down.
    /// </summary>
    [DataField]
    public SoundSpecifier OverheatLoopSound = new SoundPathSpecifier("/Audio/_NSV13/Reactor/core_overheating.ogg");

    [DataField]
    public SoundSpecifier ExplosionSound = new SoundPathSpecifier("/Audio/_NSV13/Reactor/explode.ogg");

    /// <summary>Anomalies a critically unstable core can tear loose.</summary>
    [DataField]
    public EntProtoId SurgeAnomaly = "StormdriveAnomalySurge";

    [DataField]
    public EntProtoId SheerAnomaly = "StormdriveAnomalySheer";

    [DataField]
    public EntProtoId SquallAnomaly = "StormdriveAnomalySquall";

    /// <summary>Prototype the meltdown blast uses.</summary>
    [DataField]
    public ProtoId<ExplosionPrototype> BlastExplosionType = "ReactorMeltdown";

    /// <summary>Radius of the (deliberately small) meltdown blast.</summary>
    [DataField]
    public float BlastRadius = 8f;

    [DataField]
    public float BlastCoreRadius = 4f;

    [DataField]
    public float BlastPeakIntensity = 10f;

    /// <summary>EMP radius. This, not the blast, is what ruins the ship's electronics.</summary>
    [DataField]
    public float EmpRange = 25f;

    /// <summary>
    ///     Radiation a live core puts out, in rads per degree Celsius. The figure is scaled into
    ///     SS14's damage-per-second radiation units and capped by <see cref="MaxCoreRadiation"/>.
    /// </summary>
    [DataField]
    public float RadiationPerDegree = 0.0025f;

    /// <summary>Ceiling on the live core's output, in rads per second.</summary>
    [DataField]
    public float MaxCoreRadiation = 8f;

    /// <summary>
    ///     Radiation left in the ruined core, in rads per second. SS14 applies this directly as
    ///     damage every second, so it is on a completely different scale to the BYOND original.
    /// </summary>
    /// <remarks>
    ///     Capped by <see cref="MaxCoreRadiation"/> at the use site: the gas modifier can multiply
    ///     this several times over, and a plasma-rich wreck was reading 39 rads at the core, which
    ///     killed in about three seconds. Eight is already a place you only enter in a radsuit.
    /// </remarks>
    [DataField]
    public float MeltdownRadiation = 8f;

    /// <summary>
    ///     Corium chunk spawned when the pit is cleaned out during repairs (stormdrive.dm:265).
    /// </summary>
    /// <remarks>
    ///     Only used by the repair chain. A stormdrive meltdown deliberately does NOT throw corium
    ///     around: the DM original just detonates the map's nuclear waste landmarks, and the drive
    ///     already has an explosion, an EMP pulse and a shockwave.
    /// </remarks>
    [DataField]
    public EntProtoId SlagPrototype = "RbmkSlag";

    #endregion
}

/// <summary>Mirrors NSV13's REACTOR_STATE_* defines. Values matter: they are compared with &gt;=.</summary>
public enum StormdriveState
{
    Maintenance = 1,
    Idle = 2,
    Running = 3,
    Meltdown = 4,
    Repair = 5,
    Reinforce = 6,
    Refit = 7,
}

public enum StormdriveWarning
{
    None = 0,
    Overheat = 1,
    Meltdown = 2,
}
