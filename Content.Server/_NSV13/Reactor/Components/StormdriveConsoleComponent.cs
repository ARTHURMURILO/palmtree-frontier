// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._NSV13.Reactor;

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     The stormdrive reactor control console.
/// </summary>
/// <remarks>
///     Ported from <c>/obj/machinery/computer/ship/reactor_control_computer</c>
///     (stormdrive.dm:1290-1440). The DM original linked either from a multitool buffer or from a
///     mapper-set <c>reactor_id</c>; here <see cref="ConsoleId"/> plays the role of <c>reactor_id</c>
///     and an unset id means "whatever reactor is closest on my own grid".
/// </remarks>
[RegisterComponent]
public sealed partial class StormdriveConsoleComponent : Component
{
    /// <summary>
    ///     Optional link tag, matched against <see cref="StormdriveComponent.ReactorId"/>.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string? ConsoleId;

    /// <summary>The reactor this console is currently bound to, if any.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? Reactor;

    /// <summary>How often the window is refreshed while open, in seconds.</summary>
    [DataField]
    public float UpdatePeriod = 0.5f;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextUpdate;

    /// <summary>Transient message shown in the window.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public string Notice = string.Empty;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NoticeUntil;

    [DataField]
    public TimeSpan NoticeLifetime = TimeSpan.FromSeconds(4);

    /// <summary>Seconds between two samples of the composition graph.</summary>
    [DataField]
    public float SampleInterval = 1f;

    /// <summary>How many samples the composition graph keeps.</summary>
    [DataField]
    public int HistoryLength = 100;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextSample;

    /// <summary>Rolling history per gas, indexed by <see cref="StormdriveConsoleGas"/>.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public readonly List<List<float>> GasHistory = new();
}
