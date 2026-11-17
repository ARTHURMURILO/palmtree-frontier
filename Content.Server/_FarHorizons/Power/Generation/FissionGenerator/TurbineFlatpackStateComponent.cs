namespace Content.Server._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
///     Carried by a turbine flatpack to remember the condition and tuning of the turbine it was
///     packed from, so unpacking it does not hand the crew a free repair.
/// </summary>
[RegisterComponent]
public sealed partial class TurbineFlatpackStateComponent : Component
{
    [DataField]
    public int BladeHealth;

    [DataField]
    public bool Ruined;

    [DataField]
    public float StatorLoad;

    [DataField]
    public float FlowRate;
}
