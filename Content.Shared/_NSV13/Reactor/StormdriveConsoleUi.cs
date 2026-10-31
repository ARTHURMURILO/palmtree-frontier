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

    /// <summary>Rod insertion, 0 (fully withdrawn, maximum reactivity) .. 100 (shut down).</summary>
    public float RodPercent;

    /// <summary>Waste line is open and fuel is being dumped.</summary>
    public bool PipeOpen;

    /// <summary>Last power actually delivered, in watts.</summary>
    public float LastPowerProduced;

    /// <summary>What the machine could theoretically make, for a "percent of rated" readout.</summary>
    public float TheoreticalMaximumPower;

    public float ReactionRate;

    public float TemperatureHot;
    public float TemperatureCritical;
    public float TemperatureMeltdown;

    public bool Maintenance;

    /// <summary>Plasma + constricted plasma + tritium currently in the fuel line.</summary>
    public float FuelMix;

    /// <summary>Above this the mixture is dangerously rich, matching the console warning bands.</summary>
    public float MoleThresholdHigh;

    public float MoleThresholdVeryHigh;

    public float Oxygen;
    public float Nitrogen;
    public float Plasma;
    public float ConstrictedPlasma;
    public float Tritium;
    public float CarbonDioxide;
    public float WaterVapor;
    public float HyperNoblium;
    public float NitrousOxide;
    public float Nitryl;
    public float BZ;
    public float Stimulum;
    public float Pluoxium;
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
///     Order of the fuel-line composition rows on the stormdrive console, matching the order in
///     StormdriveConsole.js so the colours and the graph line up with the original.
/// </summary>
public enum StormdriveConsoleGas
{
    ConstrictedPlasma = 0,
    Plasma = 1,
    Tritium = 2,
    Oxygen = 3,
    Nitrogen = 4,
    CarbonDioxide = 5,
    WaterVapour = 6,
    HyperNoblium = 7,
    NitrousOxide = 8,
    Nitryl = 9,
    BZ = 10,
    Stimulum = 11,
    Pluoxium = 12,
    Nucleium = 13,

    Count = 14,
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

