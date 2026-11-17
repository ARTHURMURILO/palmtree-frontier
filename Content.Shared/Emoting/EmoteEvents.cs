using Content.Shared.Chat.Prototypes;
using Content.Shared.Inventory;

namespace Content.Shared.Emoting;

public sealed class EmoteAttemptEvent(EntityUid uid) : CancellableEntityEventArgs
{
    public EntityUid Uid { get; } = uid;
}

/// <summary>
/// Broadcast after an emote has successfully been performed. Unlike <see cref="EmoteEvent"/>,
/// which is directed at the emoting entity, this event carries its source and is visible to
/// global listeners such as sensor/perception systems.
/// </summary>
public sealed class EmotePerformedEvent(EntityUid source, EmotePrototype emote) : EntityEventArgs
{
    public readonly EntityUid Source = source;
    public readonly EmotePrototype Emote = emote;
}

/// <summary>
/// An event raised just before an emote is performed, providing systems with an opportunity to cancel the emote's performance.
/// </summary>
[ByRefEvent]
public sealed class BeforeEmoteEvent(EntityUid source, EmotePrototype emote)
    : CancellableEntityEventArgs, IInventoryRelayEvent
{
    public readonly EntityUid Source = source;
    public readonly EmotePrototype Emote = emote;

    /// <summary>
    ///     The equipment that is blocking emoting. Should only be non-null if the event was canceled.
    /// </summary>
    public EntityUid? Blocker = null;

    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}
