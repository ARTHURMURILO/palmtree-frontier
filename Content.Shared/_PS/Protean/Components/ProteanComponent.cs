using Robust.Shared.GameStates;

namespace Content.Shared._PS.Protean;

/// <summary>
/// Marks the original Protean body and links it to its integrated MODsuit and controller while folded.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanComponent : Component
{
    [DataField]
    public EntityUid? Suit;

    [DataField]
    public EntityUid? Controller;

    [DataField]
    public EntityUid? PendingSuit;

    [DataField]
    public bool Folded;

    [DataField]
    public EntityUid? FoldActionEntity;

    [DataField]
    public EntityUid? LockActionEntity;

    [DataField]
    public EntityUid? InterfaceActionEntity;

    [DataField]
    public EntityUid? LowPowerActionEntity;

    [DataField]
    public EntityUid? HealActionEntity;

    // Server-side physiology timers. These are not networked; the body is authoritative.
    public TimeSpan NextRefactoryDamage;
    public TimeSpan NextRefactoryWarning;
    public TimeSpan NextOrchestratorPenalty;
    public TimeSpan NextOrchestratorWarning;
    public TimeSpan NextPassiveHeal;
}
