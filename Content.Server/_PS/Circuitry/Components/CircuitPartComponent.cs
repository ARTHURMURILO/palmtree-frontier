namespace Content.Server._PS.Circuitry.Components;

public enum CircuitPartType : byte
{
    Locomotion,
    Manipulator,
    Voice,
    Sensor,
    Analyzer,
    Radio,
    Utility,
}

public enum CircuitAnalyzerType : byte
{
    Botanical,
    Chemical,
    Medical,
    Gas,
}

/// <summary>
/// Palmtree: a physical module that grants a capability to a <see cref="CircuitAssemblyComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CircuitPartComponent : Component
{
    [DataField]
    public CircuitPartType PartType;

    // Locomotion.
    [DataField]
    public float WalkSpeed = 2.5f;

    [DataField]
    public float SprintSpeed = 4.5f;

    [DataField]
    public float Acceleration = 20f;

    [DataField]
    public float MovePowerPerMeter = 0.02f;

    [DataField]
    public bool IgnoreGravity;

    [DataField]
    public bool CanMoveInAir;

    // Manipulator.
    [DataField]
    public int Hands = 1;

    [DataField]
    public float HandRange = 1.5f;

    // Sensor / voice / generic. SensorRange is measured in tiles (standard grids have TileSize 1).
    [DataField]
    public float SensorRange = 16f;

    [DataField]
    public int MaxScanResults = 16;

    [DataField]
    public CircuitAnalyzerType AnalyzerType;

    [DataField]
    public float IdlePowerDraw = 0.05f;

    [DataField]
    public float ActionPowerCost = 0.05f;
}
