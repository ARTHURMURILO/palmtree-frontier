using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Modsuits;

[Serializable, NetSerializable]
public enum ModsuitUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ModsuitToggleMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ModsuitSealMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ModsuitModuleEntry
{
    public string Name = string.Empty;
    public byte Type;
    public int Complexity;
    public bool Active;

    public ModsuitModuleEntry()
    {
    }

    public ModsuitModuleEntry(string name, ModsuitModuleType type, int complexity, bool active)
    {
        Name = name;
        Type = (byte) type;
        Complexity = complexity;
        Active = active;
    }
}

[Serializable, NetSerializable]
public sealed class ModsuitPartEntry
{
    public string Slot = string.Empty;
    public bool Sealed;
    public bool Equipped;

    public ModsuitPartEntry()
    {
    }

    public ModsuitPartEntry(string slot, bool isSealed, bool equipped)
    {
        Slot = slot;
        Sealed = isSealed;
        Equipped = equipped;
    }
}

[Serializable, NetSerializable]
public sealed class ModsuitUiState : BoundUserInterfaceState
{
    public bool Active;
    public bool Sealed;
    public bool Open;
    public bool Infinite;
    public float Charge;
    public float MaxCharge;
    public int Complexity;
    public int ComplexityMax;
    public string Theme = string.Empty;
    public string Skin = string.Empty;
    public List<ModsuitModuleEntry> Modules = new();
    public List<ModsuitPartEntry> Parts = new();
}
