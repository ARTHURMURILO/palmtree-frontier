using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Construction;

/// <summary>
///     Completes packing a deployed machine back into its flatpack.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class PackIntoFlatpackDoAfterEvent : SimpleDoAfterEvent
{
}
