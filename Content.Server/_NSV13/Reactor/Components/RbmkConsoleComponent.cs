// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     A control console for an NSV13 RBMK reactor.
/// </summary>
/// <remarks>
///     NSV13 linked its computers to reactors by scanning every machine for a matching
///     <c>id</c> string (rbmk.dm:580-585). Here the console resolves its reactor the same way when
///     <see cref="ConsoleId"/> is set, and otherwise simply takes the nearest reactor on its own
///     grid - which is the SS14 shape of the same idea, because a grid is a ship.
/// </remarks>
[RegisterComponent]
public sealed partial class RbmkConsoleComponent : Component
{
    /// <summary>
    ///     Optional link tag. Must match <see cref="RbmkReactorComponent.ConsoleId"/>; when null the
    ///     console binds to the closest reactor on its grid.
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

    /// <summary>Transient message shown in the window; cleared <see cref="NoticeLifetime"/> after being raised.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public string Notice = string.Empty;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NoticeUntil;

    /// <summary>How long a notice stays on screen.</summary>
    [DataField]
    public TimeSpan NoticeLifetime = TimeSpan.FromSeconds(4);

    #region Telemetry history

    /// <summary>Seconds between two samples of the statistics graphs (rbmk.dm:646).</summary>
    [DataField]
    public float SampleInterval = 1f;

    /// <summary>How many samples the graphs keep; the DM console kept 100.</summary>
    [DataField]
    public int HistoryLength = 100;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextSample;

    [ViewVariables(VVAccess.ReadOnly)]
    public readonly List<float> PowerHistory = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public readonly List<float> PsiHistory = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public readonly List<float> CoolantInHistory = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public readonly List<float> CoolantOutHistory = new();

    #endregion
}
