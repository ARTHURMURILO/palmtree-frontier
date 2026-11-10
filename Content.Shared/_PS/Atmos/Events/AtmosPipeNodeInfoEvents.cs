using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Atmos.Events;

/// <summary>
/// Palmtree: asks the server for the pipe nodes of a prototype, because the client cannot read
/// <see cref="Content.Shared.NodeContainer.NodeContainerComponent.Nodes"/> on the prototype.
/// </summary>
[Serializable, NetSerializable]
public sealed class AtmosPipeNodeInfoRequestEvent(string prototypeId) : EntityEventArgs
{
    public readonly string PrototypeId = prototypeId;
}

/// <summary>
/// Palmtree: the server's answer to <see cref="AtmosPipeNodeInfoRequestEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class AtmosPipeNodeInfoResponseEvent(string prototypeId, List<AtmosPipeNodeInfo> nodes) : EntityEventArgs
{
    public readonly string PrototypeId = prototypeId;
    public readonly List<AtmosPipeNodeInfo> Nodes = nodes;
}

/// <summary>
/// Palmtree: one pipe node as the placement check sees it: its local direction and its layer.
/// </summary>
[Serializable, NetSerializable]
public sealed record AtmosPipeNodeInfo(PipeDirection Direction, AtmosPipeLayer Layer);
