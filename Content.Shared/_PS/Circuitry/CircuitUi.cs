using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Circuitry;

[Serializable, NetSerializable]
public enum CircuitAssemblyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum CircuitCpuUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class CircuitSlotState
{
    public string SlotId = string.Empty;
    public string Role = string.Empty;
    public string? ItemName;
}

[Serializable, NetSerializable]
public sealed class CircuitAssemblyState : BoundUserInterfaceState
{
    public string Name = string.Empty;
    public bool Locked;
    public string? OwnerName;
    public List<CircuitSlotState> Slots = new();
    public float Power;
    public float MaxPower;
    public bool Powered;
    public bool HasCpu;
    public bool Running;
    public string? CpuName;
    public string? Error;
    public int Hands;
    public float SensorRange;
    public List<string> RadioChannels = new();
    public bool HasVoice;
    public bool HasLocomotion;
}

[Serializable, NetSerializable]
public sealed class CircuitCpuState : BoundUserInterfaceState
{
    public string? Source;
    public bool Running;
    public bool Installed;
    public bool Powered;
    public string? Error;
    public List<string> Log = new();
    public int MaxSourceBytes = 256 * 1024;
    public string? AssemblyName;
    public float Power;
    public float MaxPower;
    public bool Locked;
}

[Serializable, NetSerializable]
public sealed class CircuitAssemblyEjectMessage : BoundUserInterfaceMessage
{
    public string SlotId;

    public CircuitAssemblyEjectMessage(string slotId)
    {
        SlotId = slotId;
    }
}

[Serializable, NetSerializable]
public sealed class CircuitAssemblyRenameMessage : BoundUserInterfaceMessage
{
    public string Name;

    public CircuitAssemblyRenameMessage(string name)
    {
        Name = name;
    }
}

[Serializable, NetSerializable]
public sealed class CircuitAssemblyLockMessage : BoundUserInterfaceMessage
{
    public bool Locked;

    public CircuitAssemblyLockMessage(bool locked)
    {
        Locked = locked;
    }
}

[Serializable, NetSerializable]
public sealed class CircuitAssemblyOpenCpuMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CircuitAssemblyRunMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CircuitAssemblyStopMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CircuitCpuSaveMessage : BoundUserInterfaceMessage
{
    public string Source;

    public CircuitCpuSaveMessage(string source)
    {
        Source = source;
    }
}

[Serializable, NetSerializable]
public sealed class CircuitCpuRunMessage : BoundUserInterfaceMessage
{
    /// <summary>
    /// Current editor contents. When present the server stores the source before starting,
    /// so Run never executes a stale saved program.
    /// </summary>
    public string? Source;

    public CircuitCpuRunMessage(string? source = null)
    {
        Source = source;
    }
}

[Serializable, NetSerializable]
public sealed class CircuitCpuStopMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CircuitCpuClearLogMessage : BoundUserInterfaceMessage
{
}
