// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._NSV13.Reactor;

/// <summary>UI key for the stormdrive reactor control console.</summary>
/// <remarks>
///     Ported from <c>/obj/machinery/computer/ship/reactor_control_computer</c>
///     (stormdrive.dm:1290-1440): one window with the rod slider, the five presets, the maintenance
///     toggle and the fuel-dump switch, plus the whole input mixture so the engineer can see what
///     the reaction is actually eating.
/// </remarks>
[Serializable, NetSerializable]
public enum StormdriveConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class StormdriveConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>False when no reactor is linked; every control is then inert.</summary>
    public bool HasReactor;

    public StormdriveConsoleState State;

    /// <summary>Core temperature in °C.</summary>
    public float Heat;

    /// <summary>Aggregate control-rod integrity, 0..100.</summary>
    public float RodIntegrity;

    /// <summary>
    ///     The hidden 0..100 stability score, now shown on the console.
    /// </summary>
    /// <remarks>
    ///     The drive has always simulated this; the original DM showed it on a floating countdown
    ///     above the reactor, which the port never brought over. Without it the only feedback was
    ///     gravity pulls and unexplained heat spikes.
    /// </remarks>
    public float ReactorStability;

    /// <summary>
    ///     Stability change over the last process tick, expressed per second. Positive is recovery.
    /// </summary>
    public float StabilityDelta;

    /// <summary>Why stability moved on the last process tick.</summary>
    public StormdriveStabilityCause StabilityCause;

    /// <summary>Stability below which heat/rate spikes can occur; sent so the window can colour itself.</summary>
    public float StabilitySpikeThreshold = 75f;

    /// <summary>Stability below which anomalies can spawn.</summary>
    public float StabilityAnomalyThreshold = 15f;

    /// <summary>Rod insertion, 0 (fully withdrawn, maximum reactivity) .. 100 (shut down).</summary>
    public float RodPercent;

    /// <summary>Waste line is open and fuel is being dumped.</summary>
    public bool PipeOpen;

    /// <summary>Last power actually delivered, in watts.</summary>
    public float LastPowerProduced;

    /// <summary>What the machine could theoretically make, for a "percent of rated" readout.</summary>
    public float TheoreticalMaximumPower;

    /// <summary>
    ///     Reaction rate, in moles per second. The reactor's own figure is per two-second process
    ///     tick, so the raw value is divided by the tick before it is sent next to a "mol/s" label.
    /// </summary>
    public float ReactionRate;

    public float TemperatureHot;
    public float TemperatureCritical;
    public float TemperatureMeltdown;

    public bool Maintenance;

    /// <summary>Plasma + tritium currently in the fuel line.</summary>
    public float FuelMix;

    /// <summary>Above this the mixture is dangerously rich, matching the console warning bands.</summary>
    public float MoleThresholdHigh;

    public float MoleThresholdVeryHigh;

    public float Oxygen;
    public float Nitrogen;
    public float Plasma;
    public float Tritium;
    public float CarbonDioxide;
    public float WaterVapor;
    public float NitrousOxide;
    public float Nucleium;
    public float TotalMoles;

    /// <summary>Seconds left before an armed meltdown fires; zero when not armed.</summary>
    public float MeltdownCountdown;

    /// <summary>Full length of the meltdown fuse, so the console can draw it as a draining bar.</summary>
    public float MeltdownDelay;

    /// <summary>Short-lived notice: a refused transition, or the SCRAM shout.</summary>
    public string Notice = string.Empty;

    /// <summary>
    ///     Per-gas history, one sample per second, indexed by <see cref="StormdriveConsoleGas"/>.
    /// </summary>
    /// <remarks>
    ///     StormdriveConsole.js plots every gas in <c>gas_records</c>; the client needs the same
    ///     rolling window to draw the composition graph.
    /// </remarks>
    public List<List<float>> GasHistory = new();
}

/// <summary>
///     Fuel-line composition rows on the stormdrive console.
/// </summary>
/// <remarks>
///     The NSV13 console listed fourteen gases. This fork has eight of them; the rest would sit at
///     0.0% forever, so they have no row. The rows stay in the original's relative order. The enum
///     is an index into the client's colour table and into <see cref="StormdriveConsoleBoundUserInterfaceState.GasHistory"/>,
///     so the three must move together.
/// </remarks>
public enum StormdriveConsoleGas
{
    Plasma = 0,
    Tritium = 1,
    Oxygen = 2,
    Nitrogen = 3,
    CarbonDioxide = 4,
    WaterVapour = 5,
    NitrousOxide = 6,
    Nucleium = 7,

    Count = 8,
}

[Serializable, NetSerializable]
public enum StormdriveConsoleState
{
    Maintenance = 1,
    Idle = 2,
    Running = 3,
    Meltdown = 4,
    Repair = 5,
    Reinforce = 6,
    Refit = 7,
}

/// <summary>
///     Why the stormdrive's stability moved on the last process tick.
/// </summary>
/// <remarks>
///     The number alone is the same problem the RBMK integrity bar had: it falls and the engineer
///     has to guess. The fuel-line conditions are all visible (the ratio and moles bars), so the
///     console can name the exact one that is bleeding stability and what to do about it.
/// </remarks>
[Serializable, NetSerializable]
public enum StormdriveStabilityCause : byte
{
    /// <summary>Nothing is moving it: rich mix at the cap, or an idle core.</summary>
    None = 0,

    /// <summary>Rich mix under the pressure line; the core is recovering at +0.5/tick.</summary>
    Recovering = 1,

    /// <summary>Rich mix, but the line is over the mole line: the recovery is being cancelled.</summary>
    RichOverpressure = 2,

    /// <summary>Fuel ratio is 12.5–25%; the reaction runs, but stability bleeds slowly.</summary>
    Lean = 3,

    /// <summary>Lean and over pressure: the fast bleed.</summary>
    LeanOverpressure = 4,

    /// <summary>Reaction starved: fuel below 12.5 %, or fewer moles than the reaction rate.</summary>
    Starved = 5,

    /// <summary>A power overload hit the core this tick.</summary>
    Overload = 6,

    /// <summary>Containment failed and an anomaly tore loose; the refund is why stability jumped up.</summary>
    Anomaly = 7,
}

/// <summary>Set the rod insertion directly, 0..100.</summary>
[Serializable, NetSerializable]
public sealed class StormdriveConsoleSetRodsMessage : BoundUserInterfaceMessage
{
    public float Percent;

    public StormdriveConsoleSetRodsMessage(float percent)
    {
        Percent = percent;
    }
}

/// <summary>
///     Use one of the DM console's five presets: 0 %, 5 %, 16 %, 33.6 % or 100 % (SCRAM).
/// </summary>
[Serializable, NetSerializable]
public sealed class StormdriveConsolePresetMessage : BoundUserInterfaceMessage
{
    public int Preset;

    public StormdriveConsolePresetMessage(int preset)
    {
        Preset = preset;
    }
}

/// <summary>Toggle maintenance mode. Only legal while the reactor is idle.</summary>
[Serializable, NetSerializable]
public sealed class StormdriveConsoleMaintenanceMessage : BoundUserInterfaceMessage
{
}

/// <summary>Toggle the fuel dump line.</summary>
[Serializable, NetSerializable]
public sealed class StormdriveConsoleDumpMessage : BoundUserInterfaceMessage
{
}

