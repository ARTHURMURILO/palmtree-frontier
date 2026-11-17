namespace Content.Server._PS.Circuitry.Components;

/// <summary>
/// Palmtree: a removable CPU item. The Lua program is stored on the item so it can be moved
/// between assemblies and survives map saves.
/// </summary>
[RegisterComponent]
public sealed partial class CircuitCpuComponent : Component
{
    [DataField]
    public string Source = string.Empty;

    [DataField]
    public int MaxSourceBytes = 256 * 1024;

    [DataField]
    public int AutoYieldInstructions = 10_000;

    [DataField]
    public float TickBudgetMilliseconds = 0.5f;

    [DataField]
    public float IdlePowerDraw = 0.25f;

    [DataField]
    public string ProgramName = "main.lua";

    [DataField]
    public string? LastEditor;
}
