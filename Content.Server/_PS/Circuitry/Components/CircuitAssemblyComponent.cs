using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._PS.Circuitry.Components;

[Flags]
public enum CircuitCapability : uint
{
    None = 0,
    Locomotion = 1 << 0,
    Flight = 1 << 1,
    Manipulator = 1 << 2,
    Voice = 1 << 3,
    Sensor = 1 << 4,
    Reagent = 1 << 5,
    Cpu = 1 << 6,
    Power = 1 << 7,
    BotanicalAnalyzer = 1 << 8,
    ChemicalAnalyzer = 1 << 9,
    MedicalAnalyzer = 1 << 10,
    Radio = 1 << 11,
    GasAnalyzer = 1 << 12,
}

public enum CircuitSlotRole : byte
{
    Cpu,
    Cell,
    Locomotion,
    Manipulator,
    Voice,
    Sensor,
    Reagent,
    Analyzer,
    Radio,
    Utility,
}

public enum CircuitWalkMode : byte
{
    Idle,
    Direct,
    Path,
}

/// <summary>
/// Palmtree: a programmable chassis. Physical modules are stored in <see cref="ItemSlots"/> and
/// this component maps each slot to a capability role. The runtime caches are rebuilt whenever a
/// slot changes.
/// </summary>
[RegisterComponent]
public sealed partial class CircuitAssemblyComponent : Component
{
    [DataField]
    public Dictionary<string, CircuitSlotRole> Slots = new();

    [ViewVariables]
    public CircuitCapability Capabilities;

    [ViewVariables]
    public EntityUid? Cpu;

    [ViewVariables]
    public EntityUid? Cell;

    [ViewVariables]
    public EntityUid? Locomotion;

    [ViewVariables]
    public EntityUid? Voice;

    [ViewVariables]
    public EntityUid? Sensor;

    [ViewVariables]
    public EntityUid? Radio;

    [ViewVariables]
    public List<EntityUid?> Beakers = new() { null, null, null, null };

    [ViewVariables]
    public List<EntityUid> Analyzers = new();

    [ViewVariables]
    public List<EntityUid> Manipulators = new();

    [ViewVariables]
    public int Hands;

    [ViewVariables]
    public float SensorRange;

    [ViewVariables]
    public int MaxScanResults;

    [ViewVariables]
    public float HandRange = 1.5f;

    [ViewVariables]
    public float IdlePowerDraw;

    [ViewVariables]
    public float MovePowerPerMeter;

    [ViewVariables]
    public float ActionPowerCost = 1f;

    [ViewVariables]
    public bool Powered;

    [ViewVariables]
    public TimeSpan NextIdlePowerDraw;

    [ViewVariables]
    public Vector2 MoveDirection;

    [ViewVariables]
    public CircuitWalkMode WalkMode;

    [ViewVariables]
    public bool WalkArrived;

    [ViewVariables]
    public EntityCoordinates? WalkTarget;

    [ViewVariables]
    public float WalkArrivalRange = 0.5f;

    [ViewVariables]
    public float MovementPowerDebt;

    [ViewVariables]
    public Vector2 LastMovePosition;

    [ViewVariables]
    public bool CanFly;

    [DataField]
    public bool Locked;

    [DataField]
    public string? OwnerUserId;

    [DataField]
    public string? OwnerName;
}

/// <summary>
/// Raised on a chassis whenever its installed module set or power state is recomputed.
/// </summary>
public sealed class CircuitAssemblyChangedEvent : EntityEventArgs
{
}
