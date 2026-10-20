using Robust.Shared.GameStates;

namespace Content.Shared._PS.Protean;

/// <summary>
/// Marker for a Protean synthetic organ.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanOrganComponent : Component;

/// <summary>
/// The Protean core-brain. When dead, the Protean cannot unfold until repaired.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanBrainComponent : Component
{
    [DataField]
    public bool Dead;

    [DataField]
    public TimeSpan RepairDuration = TimeSpan.FromMinutes(5);
}

/// <summary>
/// The refactory organ's metal store. This becomes the suit's charge source in the advanced pass.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanRefactoryComponent : Component
{
    [DataField]
    public float Metal = 10f;

    [DataField]
    public float MaxMetal = 10f;
}

/// <summary>
/// The orchestrator organ; its absence will eventually cause movement penalties.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanOrchestratorComponent : Component;
