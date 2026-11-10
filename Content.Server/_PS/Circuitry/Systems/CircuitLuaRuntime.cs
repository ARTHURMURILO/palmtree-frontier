using Content.Server._PS.Circuitry.Lua;

namespace Content.Server._PS.Circuitry.Systems;

/// <summary>
/// Palmtree: per-assembly Lua runtime state. Handles are deliberately short-lived and opaque;
/// scripts never see raw entity IDs.
/// </summary>
public sealed class CircuitLuaRuntime
{
    public const int MaxHandles = 256;
    public const int MaxQueuedEvents = 16;

    /// <summary>
    /// Sensor lookups are rate-limited by a small token bucket instead of a hard cooldown so a
    /// script can do a scan followed by one or two finds without waiting, while a tight loop still
    /// cannot rescan thousands of times per tick.
    /// </summary>
    public const int MaxSensorQueryTokens = 4;
    public const double SensorQueriesPerSecond = 4;

    public EntityUid Assembly;
    public EntityUid Cpu;
    public LuaProgram Program = null!;
    public MoonSharp.Interpreter.Script Script = null!;

    public readonly Dictionary<int, EntityUid> Handles = new();
    public readonly Dictionary<EntityUid, int> ReverseHandles = new();
    public readonly Queue<CircuitEvent> Events = new();

    public int NextHandle = 1;
    public double SensorQueryTokens = MaxSensorQueryTokens;
    public TimeSpan LastSensorQuery;
    public TimeSpan NextWalk;
    public TimeSpan NextChat;
    public TimeSpan NextRadio;
    public TimeSpan NextSignal;
    public TimeSpan NextAction;
    public TimeSpan NextTransfer;

    public int GetHandle(EntityUid uid)
    {
        if (uid == Assembly)
            return 0;

        if (ReverseHandles.TryGetValue(uid, out var existing))
            return existing;

        if (Handles.Count >= MaxHandles)
            return -1;

        var handle = NextHandle++;
        Handles[handle] = uid;
        ReverseHandles[uid] = handle;
        return handle;
    }

    public bool TryResolve(int handle, out EntityUid uid)
    {
        if (handle == 0)
        {
            uid = Assembly;
            return uid.IsValid();
        }

        return Handles.TryGetValue(handle, out uid) && uid.IsValid();
    }

    public void QueueEvent(CircuitEvent ev)
    {
        if (Events.Count >= MaxQueuedEvents)
            Events.Dequeue();

        Events.Enqueue(ev);
    }
}

public sealed record CircuitEvent(
    string Type,
    EntityUid Source,
    string Message,
    string? Channel,
    string? Port = null,
    bool? State = null,
    Dictionary<string, float>? Damage = null,
    EntityUid? Item = null,
    string? Emote = null);
