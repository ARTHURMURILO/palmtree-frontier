// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._NSV13.Reactor;

/// <summary>
///     Which face of a reactor machine each of its connections sits on.
/// </summary>
/// <remarks>
///     The reactor art is symmetric and has no stubs, and the pipe nodes that actually carry the
///     gas live on the server only (<see cref="Content.Shared.NodeContainer.NodeContainerComponent.Nodes"/>
///     is <c>serverOnly</c>). Without this, a client has no way to tell the coolant feed from the
///     outlet: the sprite looks the same whichever way the machine was spawned, and the only hint
///     is a line of examine text. Copying just the faces to the client lets the examine overlay
///     point at them, which is cheaper than redrawing the art.
///
///     Faces are in the machine's own frame. The client rotates them by the machine's world
///     rotation, so a reactor bolted down on a turned hull still reports the side the player sees.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ReactorPortLayoutComponent : Component
{
    /// <summary>Face the cold coolant is piped into.</summary>
    [DataField, AutoNetworkedField]
    public Direction CoolantIn = Direction.Invalid;

    /// <summary>Face the hot coolant leaves from.</summary>
    [DataField, AutoNetworkedField]
    public Direction CoolantOut = Direction.Invalid;

    /// <summary>Face the moderator feed is piped into. Anything fed here is destroyed.</summary>
    [DataField, AutoNetworkedField]
    public Direction Moderator = Direction.Invalid;
}
