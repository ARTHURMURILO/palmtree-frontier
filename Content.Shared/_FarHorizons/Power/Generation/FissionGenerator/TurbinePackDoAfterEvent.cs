using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
///     Completes packing a gas turbine back into its flatpack.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class TurbinePackDoAfterEvent : SimpleDoAfterEvent
{
}
