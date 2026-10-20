using Robust.Shared.GameStates;

namespace Content.Shared._PS.Protean;

/// <summary>
/// Marks a MODsuit as a Protean's integrated suit.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanSuitComponent : Component
{
    [DataField]
    public EntityUid? Owner;

    [DataField]
    public EntityUid? Controller;

    [DataField]
    public bool Locked;

    /// <summary>
    /// Transient bypass used by the fold runtime to remove the otherwise integrated suit.
    /// </summary>
    public bool AllowRemoval;

    [DataField]
    public EntityUid? StoredSuit;
}
