using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._NSV13.Reactor;

/// <summary>
///     Completes the packing-up of a cold, shut-down reactor.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ReactorDeconstructDoAfterEvent : SimpleDoAfterEvent
{
}
