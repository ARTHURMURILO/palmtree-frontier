namespace Content.Shared.NodeContainer;

/// <summary>
/// Palmtree: raised after a <see cref="NodeContainerComponent"/>'s nodes have been initialized and
/// started, including entity deserialization. Component-specific <c>ComponentStartup</c>
/// subscriptions are single-handler, so this broadcast hook lets extra systems react without
/// replacing <c>NodeContainerSystem</c>'s own subscription.
/// </summary>
public sealed class NodeContainerStartedEvent(EntityUid uid) : EntityEventArgs
{
    public readonly EntityUid Uid = uid;
}
