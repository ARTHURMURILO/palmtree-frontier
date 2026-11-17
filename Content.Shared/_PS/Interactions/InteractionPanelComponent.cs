using Robust.Shared.GameStates;

namespace Content.Shared._PS.Interactions;

/// <summary>
/// Marker component for mobs that can use the interaction panel (as actor and/or target).
/// Attached to organic humanoids in <c>Resources/Prototypes/Entities/Mobs/Species/base.yml</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class InteractionPanelComponent : Component
{
    /// <summary>
    /// Palmtree: client draw-depth preference for this character (below/default/above mobs).
    /// </summary>
    [DataField, AutoNetworkedField]
    public CharacterLayerMode LayerMode = CharacterLayerMode.Default;
}
