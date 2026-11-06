using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.Atmos.Components;

/// <summary>
/// Networked copy of the pipe node directions on an entity, indexed by atmos layer.
/// </summary>
/// <remarks>
/// Palmtree: <see cref="Content.Shared.NodeContainer.NodeContainerComponent.Nodes"/> is
/// <c>serverOnly</c>, so the RPD placement ghost cannot inspect an anchored pipe's nodes directly.
/// The server fills this summary from its authoritative nodes; the client uses it to run the same
/// same-layer, node-direction overlap check the server runs before a placement.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AtmosPipeNodeDirectionsComponent : Component
{
    [DataField, AutoNetworkedField]
    public PipeDirection Primary = PipeDirection.None;

    [DataField, AutoNetworkedField]
    public PipeDirection Secondary = PipeDirection.None;

    [DataField, AutoNetworkedField]
    public PipeDirection Tertiary = PipeDirection.None;

    public PipeDirection GetDirections(AtmosPipeLayer layer)
    {
        return layer switch
        {
            AtmosPipeLayer.Primary => Primary,
            AtmosPipeLayer.Secondary => Secondary,
            AtmosPipeLayer.Tertiary => Tertiary,
            _ => PipeDirection.None,
        };
    }
}
