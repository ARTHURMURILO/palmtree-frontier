using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Protean;

[Serializable, NetSerializable]
public enum ProteanUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum ProteanUiAction : byte
{
    ToggleFold,
    ToggleLock,
    EjectSuit,
    ServoSpeed,
    ToggleLowPower,
    Heal,
}

[Serializable, NetSerializable]
public sealed class ProteanActionMessage(ProteanUiAction action) : BoundUserInterfaceMessage
{
    public ProteanUiAction Action { get; } = action;
}

[Serializable, NetSerializable]
public sealed class ProteanUiState : BoundUserInterfaceState
{
    public float Metal;
    public float MaxMetal = 10f;
    public bool Folded;
    public bool Locked;
    public bool HasSuit;
    public bool CanFold;
    public bool CanUnfold;
    public bool HasStoredSuit;
    public bool HasServo;
    public bool ServoReady;
    public bool LowPower;
    public bool CanHeal;
}
