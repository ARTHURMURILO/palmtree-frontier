// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._NSV13.Reactor;

/// <summary>
///     UI key for the RBMK consoles.
/// </summary>
/// <remarks>
///     NSV13 spreads the reactor over four separate computers - control rods, statistics, fuel
///     management and pump signalling (rbmk.dm:580-700). There is no radio-signal bus in this port
///     and the pump consoles only existed to command remote pumps that are now regular pipes, so
///     everything a crew actually needs is folded into one console: the same setpoint, the same
///     telemetry and the same rod handling, one window.
/// </remarks>
[Serializable, NetSerializable]
public enum RbmkConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class RbmkConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>False when the console has no reactor linked; every control is then inert.</summary>
    public bool HasReactor;

    public bool Running;
    public bool Slagged;

    /// <summary>Current reactivity, in K.</summary>
    public float K;

    /// <summary>Setpoint, 0 (rods fully inserted) .. 3 (rods fully withdrawn).</summary>
    public float DesiredK;

    /// <summary>
    ///     Derived rod insertion, exactly as the DM console displayed it:
    ///     <c>100 - desired_k / 3 * 100</c>.
    /// </summary>
    public float RodPercent;

    /// <summary>Core temperature in °C.</summary>
    public float Temperature;

    /// <summary>Coolant loop pressure in PSI.</summary>
    public float Pressure;

    /// <summary>Power, as a percentage of rated output.</summary>
    public float Power;

    public float VesselIntegrity;
    public float MaxVesselIntegrity;

    /// <summary>What is eating the vessel right now.</summary>
    /// <remarks>
    ///     An integrity bar that only ever goes down tells the engineer nothing they did not already
    ///     know. This tells them what to fix: "coolant feed starved" is a pump problem, "core over
    ///     meltdown temperature" is a rod/heat problem, "outlet over pressure" is a venting problem.
    /// </remarks>
    public RbmkIntegrityCause IntegrityCause;

    /// <summary>Integrity lost on the last tick. Zero when the vessel is holding.</summary>
    public float IntegrityDamage;

    /// <summary>Fraction of the coolant demand the inlet is meeting, 0..1.</summary>
    public float CoolantSatisfaction;

    /// <summary>Coolant temperature entering the core, °C.</summary>
    public float CoolantIn;

    /// <summary>Coolant temperature leaving the core, °C.</summary>
    public float CoolantOut;

    /// <summary>Rads per second coming off the core.</summary>
    public float Radiation;

    public int FuelRods;
    public int MaxFuelRods;

    public List<RbmkConsoleFuelRod> Rods = new();

    // Thresholds, so the window can colour its own readouts without hard-coding numbers.
    public float TemperatureOperating;
    public float TemperatureCritical;
    public float TemperatureMeltdown;
    public float PressureCritical;

    /// <summary>
    ///     Short-lived notice - a refused action, mostly. Empty most of the time; the server clears
    ///     it a few seconds after it was raised.
    /// </summary>
    public string Notice = string.Empty;

    #region Telemetry history

    /// <summary>
    ///     Rolling one-sample-per-second history driving the four graphs on the statistics page.
    /// </summary>
    /// <remarks>
    ///     The DM statistics console kept four lists of a hundred samples and replayed them into a
    ///     tgui <c>Chart.Line</c> (rbmk.dm:646-672). SS14 has no chart control, so the samples are
    ///     shipped to the client and drawn by <c>ReactorChart</c>; the arrays are trimmed server-side
    ///     to <c>ReactorChart.HistoryLength</c> so the payload stays small.
    /// </remarks>
    public List<float> PowerHistory = new();

    public List<float> PsiHistory = new();

    public List<float> CoolantInHistory = new();

    public List<float> CoolantOutHistory = new();

    #endregion
}

/// <summary>
///     What is currently damaging the reactor vessel.
/// </summary>
/// <remarks>
///     The DM original printed none of this: the integrity number dropped and the engineer guessed.
///     An RBMK starting from cold drops integrity for one specific reason (the coolant feed is not
///     meeting demand yet), and saying so turns a mysterious leak into a plumbing task.
/// </remarks>
[Serializable, NetSerializable]
public enum RbmkIntegrityCause : byte
{
    /// <summary>Vessel is holding; nothing is applying damage this tick.</summary>
    None = 0,

    /// <summary>Coolant feed is below the minimum the core needs and the rods are still hot.</summary>
    CoolantStarvation = 1,

    /// <summary>Core temperature is past the meltdown threshold.</summary>
    OverTemperature = 2,

    /// <summary>Outlet pressure is past the critical threshold.</summary>
    OverPressure = 3,
}

[Serializable, NetSerializable]
public sealed class RbmkConsoleFuelRod
{
    /// <summary>
    ///     Index into the reactor's <c>FuelRods</c> list. Sent explicitly because the list the
    ///     window draws can skip rods whose entity is gone; a client-side row index would then
    ///     eject the wrong rod.
    /// </summary>
    public int Index;

    public string Name = string.Empty;

    /// <summary>Remaining fuel power contribution.</summary>
    public float FuelPower;

    /// <summary>Starting fuel power, for a "how burnt is this rod" bar.</summary>
    public float OriginalFuelPower;

    /// <summary>Accumulated depletion, 0 .. <see cref="DepletionThreshold"/>.</summary>
    public float Depletion;

    public float DepletionThreshold;
}

/// <summary>Move the rod setpoint. Clamped to 0..3 by the server.</summary>
[Serializable, NetSerializable]
public sealed class RbmkConsoleSetTargetMessage : BoundUserInterfaceMessage
{
    public float Target;

    public RbmkConsoleSetTargetMessage(float target)
    {
        Target = target;
    }
}

/// <summary>Eject the fuel rod at <see cref="Index"/> out onto the floor next to the reactor.</summary>
[Serializable, NetSerializable]
public sealed class RbmkConsoleEjectRodMessage : BoundUserInterfaceMessage
{
    public int Index;

    public RbmkConsoleEjectRodMessage(int index)
    {
        Index = index;
    }
}

