using System.Diagnostics.CodeAnalysis;
using Robust.Shared.GameStates;

namespace Content.Shared.Mind.Components;

/// <summary>
/// This component indicates that this entity may have mind, which is simply an entity with a <see cref="MindComponent"/>.
/// The mind entity is not actually stored in a "container", but is simply stored in nullspace.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState] // Palmtree/Coyote: dropped Access(typeof(SharedMindSystem)) for ever-mind tracking
public sealed partial class MindContainerComponent : Component
{
    /// <summary>
    ///     The mind controlling this mob. Can be null.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Mind { get; set; }

    /// <summary>
    ///     True if we have a mind, false otherwise.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Mind))]
    public bool HasMind => Mind != null;

    /// <summary>
    ///     Whether examining should show information about the mind or not.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("showExamineInfo"), AutoNetworkedField]
    public bool ShowExamineInfo { get; set; }

    // Palmtree/Coyote: ever-mind tracking (Coyote commit 102b623d04e)
    /// <summary>
    ///     Whether this entity has ever had a mind. Used to tell player-owned bodies (including
    ///     ghosted corpses) apart from loot, e.g. for expedition body recovery.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField, AutoNetworkedField]
    public bool HasHadMind = false;
    // End Palmtree/Coyote

    /// <summary>
    ///     Whether the mind will be put on a ghost after this component is shutdown.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("ghostOnShutdown")]
    public bool GhostOnShutdown { get; set; } = true;
}

public abstract class MindEvent : EntityEventArgs
{
    public readonly Entity<MindComponent> Mind;
    public readonly Entity<MindContainerComponent> Container;

    public MindEvent(Entity<MindComponent> mind, Entity<MindContainerComponent> container)
    {
        Mind = mind;
        Container = container;
    }
}

/// <summary>
/// Event raised directed at a mind-container when a mind gets removed.
/// </summary>
public sealed class MindRemovedMessage : MindEvent
{
    public MindRemovedMessage(Entity<MindComponent> mind, Entity<MindContainerComponent> container)
        : base(mind, container)
    {
    }
}

/// <summary>
/// Event raised directed at a mind when it gets removed from a mind-container.
/// </summary>
public sealed class MindGotRemovedEvent : MindEvent
{
    public MindGotRemovedEvent(Entity<MindComponent> mind, Entity<MindContainerComponent> container)
        : base(mind, container)
    {
    }
}

/// <summary>
/// Event raised directed at a mind-container when a mind gets added.
/// </summary>
public sealed class MindAddedMessage : MindEvent
{
    public MindAddedMessage(Entity<MindComponent> mind, Entity<MindContainerComponent> container)
        : base(mind, container)
    {
    }
}

/// <summary>
/// Event raised directed at a mind when it gets added to a mind-container.
/// </summary>
public sealed class MindGotAddedEvent : MindEvent
{
    public MindGotAddedEvent(Entity<MindComponent> mind, Entity<MindContainerComponent> container)
        : base(mind, container)
    {
    }
}
