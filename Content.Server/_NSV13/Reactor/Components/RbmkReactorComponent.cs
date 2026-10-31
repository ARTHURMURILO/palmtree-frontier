// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Atmos.Piping.Trinary.Components;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Atmos;
using Content.Shared.Explosion;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     NSV13 "RBMK" / AGCNR — an advanced gas-cooled nuclear reactor.
/// </summary>
/// <remarks>
///     Ported from BeeStation/NSV13 <c>nsv13/code/modules/power/reactor/rbmk.dm</c> (889 lines).
///     The DM original is a trinary atmospherics component with three gates:
///     <c>airs[1]</c> coolant in, <c>airs[2]</c> moderator in, <c>airs[3]</c> coolant out.
///     That maps 1:1 onto a three-port atmos device, so this component stores node names the
///     same way <see cref="GasMixerComponent"/> does.
///
///     Units are kept exactly as the original to keep the port auditable:
///     <see cref="Temperature"/> is <b>degrees Celsius</b> and <see cref="Pressure"/> is <b>PSI</b>.
///     SS14's <see cref="GasMixture"/> works in Kelvin and kPa, so conversions happen at the boundary.
/// </remarks>
[RegisterComponent]
public sealed partial class RbmkReactorComponent : Component
{
    #region Atmos nodes

    /// <summary>Coolant inlet. Heats up, then the whole mixture is pushed to the outlet.</summary>
    [DataField]
    public string CoolantInputName = "coolantIn";

    /// <summary>Moderator inlet. Everything fed here is destroyed each tick.</summary>
    [DataField]
    public string ModeratorInputName = "moderatorIn";

    /// <summary>Coolant outlet. Forced to the core temperature every tick.</summary>
    [DataField]
    public string CoolantOutputName = "coolantOut";

    #endregion

    #region Core physics

    /// <summary>Core temperature in degrees Celsius. Losing control of this means meltdown.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float Temperature;

    /// <summary>Output pressure in PSI, recomputed each tick from the coolant outlet mixture.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float Pressure;

    /// <summary>Current reactivity ("rate of reaction"), clamped to <c>[0, MaxCriticality]</c>.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float K;

    /// <summary>Reactivity setpoint the control rods chase. Player-settable 0..MaxCriticality.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float DesiredK;

    /// <summary>Structural hit points. Damage drives the reactor towards meltdown / blowout.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float VesselIntegrity = 400f;

    /// <summary>The <c>initial(vessel_integrity)</c> the DM repair gates are written against.</summary>
    [DataField]
    public float MaxVesselIntegrity = 400f;

    /// <summary>
    ///     Why the vessel took structural damage on the most recent tick, or <see cref="RbmkIntegrityCause.None"/>.
    /// </summary>
    /// <remarks>
    ///     The integrity bar on its own is unhelpful: it drops, and the engineer has to guess whether
    ///     the core is too hot, the outlet is over pressure, or the coolant feed has run dry. This is
    ///     the answer the console prints next to the bar. When several causes apply on one tick the
    ///     one that did the most damage wins, because that is the one worth fixing first.
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public RbmkIntegrityCause LastIntegrityCause = RbmkIntegrityCause.None;

    /// <summary>Damage the vessel took on the most recent tick, for a per-second rate readout.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float LastIntegrityDamage;

    /// <summary>
    ///     Largest single hit taken this tick, used only to pick which cause to report.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float LargestIntegrityHit;

    /// <summary>How much of the coolant demand is actually being met, 0..1. Reported to the UI.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float CoolantSatisfaction = 1f;

    /// <summary>Core output as a percentage of <see cref="TemperatureCritical"/>.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float Power;

    /// <summary>Watts produced on the last tick, fed into the power net.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float LastPowerProduced;

    /// <summary>Last tick's coolant heat delta, for the UI.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float LastHeatDelta;

    /// <summary>Terminal state. Once true the reactor stops processing permanently.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Slagged;

    #endregion

    #region Coolant / moderation

    /// <summary>
    ///     Multiplier on coolant heat transfer. Reset to <see cref="GasAbsorptionConstant"/> each tick,
    ///     then raised by the permeability gases (BZ / water vapour / hyper-noblium).
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float GasAbsorptionEffectiveness = 0.5f;

    /// <summary>
    ///     Per-round baseline, snapshotted on init. Randomised to 0.5 or 0.6 so that
    ///     "every reactor is slightly different", exactly as the original does.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float GasAbsorptionConstant = 0.5f;

    /// <summary>Moles of coolant needed before the core is considered fed.</summary>
    [DataField]
    public float MinimumCoolantLevel = 5f;

    /// <summary>Consecutive ticks without enough coolant. Decays twice as fast as it accumulates.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int NoCoolantTicks;

    [ViewVariables(VVAccess.ReadOnly)]
    public float LastCoolantTemperature;

    [ViewVariables(VVAccess.ReadOnly)]
    public float LastOutputTemperature;

    /// <summary>Loaded fuel rods, max <see cref="MaxFuelRods"/>.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> FuelRods = new();

    /// <summary>
    ///     Container the loaded rods live in while installed. The DM reactor kept them in its own
    ///     <c>contents</c>, so they had to keep existing: depleting them, converting them and the
    ///     fuel console all read the rod entity itself.
    /// </summary>
    public const string FuelContainer = "rbmk_fuel";

    [DataField]
    public int MaxFuelRods = 5;

    #endregion

    #region Precharge

    /// <summary>
    ///     Stand-in for a plumbed-in coolant loop and moderator feed.
    /// </summary>
    /// <remarks>
    ///     A reactor on a real ship is surrounded by pumps, a radiator loop and a plasma line; the
    ///     "pre-started" mapping prototype has none of that, so it would either sit inert or cook
    ///     itself within a minute. When set, every tick the coolant inlet is refilled with cold gas
    ///     (as if a heat exchanger had cooled it) and the moderator gate is topped up (as if a pump
    ///     were feeding it). It is a convenience for testing and for spawn-menu reactors, not a
    ///     physics change: the mass balance of a plumbed reactor is untouched.
    /// </remarks>
    [DataField]
    public bool Precharged;

    /// <summary>Moles of coolant restocked in the inlet each tick while <see cref="Precharged"/>.</summary>
    [DataField]
    public float PrechargeCoolantMoles = 800f;

    /// <summary>
    ///     Temperature the restocked coolant arrives at, in Celsius. The heat exchanger in the
    ///     original loop is what makes this cold; -175 °C roughly balances one unit of K per tick.
    /// </summary>
    [DataField]
    public float PrechargeCoolantCelsius = -175f;

    /// <summary>
    ///     Moles of plasma restocked in the moderator gate each tick while precharged. Kept small on
    ///     purpose: everything in the gate adds <c>moles / 1000</c> of raw reactivity on top of the
    ///     control rods, so a tankful per tick would pin the core at maximum criticality.
    /// </summary>
    [DataField]
    public float PrechargeModeratorMoles = 100f;

    #endregion

    #region Control

    /// <summary>Maximum change in <see cref="K"/> per tick.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float ControlRodEffectiveness = 0.65f;

    /// <summary>Console link tag.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string? ConsoleId;

    #endregion

    #region Tuning

    [DataField]
    public float TemperatureOperating = 640f;

    [DataField]
    public float TemperatureCritical = 800f;

    [DataField]
    public float TemperatureMeltdown = 900f;

    [DataField]
    public float PressureCritical = 1469.59f;

    [DataField]
    public float MaxCriticality = 3f;

    /// <summary>Turns the reactor's kilowatts into something usable.</summary>
    [DataField]
    public float BasePowerModifier = 8000f;

    /// <summary>Ticks the core survives without coolant before it starts eating itself.</summary>
    [DataField]
    public int NoCoolantTolerance = 5;

    /// <summary>Seconds between simulation ticks. NSV13 runs this at 2 s, not the 1 s its comments claim.</summary>
    [DataField]
    public float TickInterval = 2f;

    #endregion

    #region Runtime

    /// <summary>
    ///     Fuel is loaded and the core is ticking. Set by the prototype for the pre-started reactor,
    ///     and by <see cref="RbmkReactorSystem"/> when the last rod is pulled.
    /// </summary>
    /// <remarks>
    ///     Deliberately not called <c>Running</c>: <see cref="Component.Running"/> already means
    ///     "this component's lifecycle is active", and hiding it would make the two impossible to
    ///     tell apart for anything holding an <see cref="IComponent"/>. The explicit data-field name
    ///     keeps the prototype key as <c>running</c>.
    /// </remarks>
    [DataField("running")]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool IsRunning;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextTick;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool AlertActive;

    public TimeSpan NextFlicker;

    /// <summary>
    ///     How often an overloaded core (power ≥ 90 %) makes the ship's lights stutter. The DM
    ///     original used 1.5 minutes; that is far too long to see, so this is the tuned value.
    /// </summary>
    [DataField]
    public TimeSpan FlickerInterval = TimeSpan.FromSeconds(45);

    /// <summary>Cosmetic steam puff spawned while the core is over pressure.</summary>
    [DataField]
    public EntProtoId VentEffect = "EffectRbmkVentSteam";

    /// <summary>
    ///     Startup sting, picked at random - rbmk.dm:540 <c>pick(startup.ogg, startup2.ogg)</c>. The
    ///     RBMK shares the ship reactor's startup sounds with the stormdrive in the original.
    /// </summary>
    [DataField]
    public List<SoundSpecifier> StartupSounds = new()
    {
        new SoundPathSpecifier("/Audio/_NSV13/Reactor/startup.ogg"),
        new SoundPathSpecifier("/Audio/_NSV13/Reactor/startup2.ogg"),
    };

    /// <summary>Rod handling clicks (rbmk.dm:692,730 <c>switch*.ogg</c>).</summary>
    [DataField]
    public List<SoundSpecifier> SwitchSounds = new()
    {
        new SoundPathSpecifier("/Audio/_NSV13/Reactor/rbmk/switch.ogg"),
        new SoundPathSpecifier("/Audio/_NSV13/Reactor/rbmk/switch2.ogg"),
        new SoundPathSpecifier("/Audio/_NSV13/Reactor/rbmk/switch3.ogg"),
    };

    /// <summary>Looping alarm raised when the core enters an alert state.</summary>
    [DataField]
    public SoundSpecifier AlarmSound = new SoundPathSpecifier("/Audio/_NSV13/Reactor/alarm.ogg");

    /// <summary>Handle for the looping alarm, so it can be silenced when the core recovers.</summary>
    [ViewVariables]
    public EntityUid? AlarmStream;

    /// <summary>Played when the core melts down.</summary>
    [DataField]
    public SoundSpecifier MeltdownSound = new SoundPathSpecifier("/Audio/_NSV13/Reactor/meltdown.ogg");

    /// <summary>Played when the vessel blows out.</summary>
    [DataField]
    public SoundSpecifier ExplosionSound = new SoundPathSpecifier("/Audio/_NSV13/Reactor/explode.ogg");

    #endregion

    #region Failure

    /// <summary>
    ///     Explosion prototype used by <c>meltdown()</c>. Defined in
    ///     <c>Resources/Prototypes/_NSV13/Explosions/explosions.yml</c>. It is the only prototype
    ///     that carries <c>Structural</c> damage, which is what lets the blast actually tear hull
    ///     rather than just scorching it.
    /// </summary>
    [DataField]
    public ProtoId<ExplosionPrototype> BlastExplosionType = "ReactorMeltdown";

    /// <summary>Radius, in tiles, at which the meltdown blast has fallen off to nothing.</summary>
    /// <remarks>
    ///     Kept small on purpose. The destructive part of a meltdown is meant to be the reactor
    ///     compartment, not the ship; everything past this radius is felt through
    ///     <see cref="ShockwaveRadius"/>, which is non-destructive.
    /// </remarks>
    [DataField]
    public float BlastRadius = 16f;

    /// <summary>Tiles around the core that take the full blast intensity.</summary>
    [DataField]
    public float BlastCoreRadius = 8f;

    /// <summary>
    ///     Intensity at the epicentre. Together with <see cref="BlastCoreRadius"/> this sets the
    ///     slope, so the blast is at full strength out to the core radius and then tapers.
    ///     This is the main dial for "how hard does the hull get opened up".
    /// </summary>
    [DataField]
    public float BlastPeakIntensity = 12f;

    /// <summary>
    ///     How far the non-destructive shockwave reaches. Deliberately much larger than the blast:
    ///     the whole ship should feel the hit even when the hull survives, which is the point.
    /// </summary>
    [DataField]
    public float ShockwaveRadius = 38f;

    /// <summary>
    ///     Knockback speed, in tiles per second, applied at the shockwave origin. Scaled by each
    ///     body's mass, so a toolbox and a crewman both get shoved at about this speed rather than
    ///     the toolbox being flung into orbit.
    /// </summary>
    [DataField]
    public float ShockwaveSpeed = 7f;

    /// <summary>Knockdown applied at the epicentre, scaled down linearly with distance.</summary>
    [DataField]
    public TimeSpan ShockwaveKnockdown = TimeSpan.FromSeconds(3);

    /// <summary>How many chunks of corium are flung across the ship.</summary>
    [DataField]
    public int SlagCount = 18;

    /// <summary>Speed the slag chunks are thrown at.</summary>
    [DataField]
    public float SlagThrowSpeed = 12f;

    /// <summary>Prototype flung across the grid by the slag storm.</summary>
    [DataField]
    public EntProtoId SlagPrototype = "RbmkSlag";

    /// <summary>Chance per light fixture on the ship that the surge destroys the bulb.</summary>
    [DataField]
    public float LightDestroyChance = 0.6f;

    /// <summary>Chance per surviving fixture that it starts flickering instead.</summary>
    [DataField]
    public float LightFlickerChance = 0.9f;

    /// <summary>
    ///     Radiation a live core puts out, in rads per degree Celsius of core temperature.
    /// </summary>
    /// <remarks>
    ///     The DM original calls <c>radiation_pulse(temperature * spice)</c> once per tick, but BYOND
    ///     radiation is a slow accumulator with its own scale. SS14 has no such accumulator: the
    ///     <see cref="RadiationSourceComponent"/> intensity is handed straight to the damage system
    ///     once a second, so the same number read directly is instantly lethal. A running core at
    ///     ~700 °C has to land in the "you can hear it ticking" range of 1-2 rads per second, which
    ///     is what this factor is for.
    /// </remarks>
    [DataField]
    public float RadiationPerDegree = 0.0025f;

    /// <summary>
    ///     Ceiling on the core's own output, in rads per second. Without it a runaway core reads
    ///     thousands of rads a second and there is no warning at all - the crew just drops dead.
    /// </summary>
    [DataField]
    public float MaxCoreRadiation = 8f;

    /// <summary>
    ///     Radiation left behind in the slagged core, in rads per second.
    /// </summary>
    /// <remarks>
    ///     SS14 hands <c>Intensity - Slope * distance</c> straight to the damage system once a second,
    ///     so this number <em>is</em> damage per second at the core. It is not the DM/BYOND scale:
    ///     a human has ~100 health, so anything in the hundreds kills instantly with no time to say
    ///     anything. 25 rads is "you have a few seconds, and the Geiger counter is screaming", and
    ///     radiation sources add up, so the room reads a little higher than the core alone.
    /// </remarks>
    [DataField]
    public float MeltdownRadiation = 25f;

    #endregion
}
