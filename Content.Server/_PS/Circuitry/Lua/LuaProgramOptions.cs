using System;

namespace Content.Server._PS.Circuitry.Lua;

/// <summary>
/// Lifecycle of a single player-authored Lua program.
/// </summary>
public enum LuaProgramState
{
    Idle,
    Loading,
    Running,
    Waiting,
    Finished,
    Error,
    Stopped,
}

/// <summary>
/// Limits and budgets for the server-side Lua runtime.
/// </summary>
public sealed class LuaProgramOptions
{
    /// <summary>Maximum program size in UTF-8 bytes.</summary>
    public int MaxSourceBytes { get; init; } = 256 * 1024;

    /// <summary>Instructions executed before MoonSharp force-yields the coroutine.</summary>
    public int AutoYieldInstructions { get; init; } = 10_000;

    /// <summary>Maximum wall-clock time spent resuming one program in a single tick.</summary>
    public TimeSpan TickBudget { get; init; } = TimeSpan.FromMilliseconds(0.5);

    /// <summary>
    /// Maximum time a program may run without calling robot.wait() before it is stopped.
    /// Prevents busy loops from consuming a slice of every tick forever.
    /// </summary>
    public TimeSpan MaxNoWaitTime { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum single wait duration, clamped by the runtime.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Number of console lines retained for the UI.</summary>
    public int MaxLogLines { get; init; } = 100;

    /// <summary>Maximum size of a string produced by guarded standard-library functions.</summary>
    public int MaxStringBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Maximum managed allocation observed during one coroutine resume. This is a
    /// coarse watchdog, not a hard memory quota.
    /// </summary>
    public long MaxAllocationBytesPerResume { get; init; } = 8 * 1024 * 1024;
}
