using Robust.Shared.GameStates;

namespace Content.Shared._PS.Protean;

/// <summary>
/// Invisible player-controlled mob used while a Protean is folded into its suit.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanControllerComponent : Component
{
    [DataField]
    public EntityUid? OriginalBody;

    [DataField]
    public EntityUid? Suit;

    [DataField]
    public EntityUid? UnfoldActionEntity;

    [DataField]
    public EntityUid? LockActionEntity;

    [DataField]
    public EntityUid? InterfaceActionEntity;

    [DataField]
    public EntityUid? HealActionEntity;
}
