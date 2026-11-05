using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._PS.Clothing;

/// <summary>
///     Allows rings to be stored in a pair of gloves. The number of ring slots is configurable so
///     the one-ring-per-glove limit can be changed later without rewriting the system.
/// </summary>
[RegisterComponent]
public sealed partial class RingHolderComponent : Component
{
    public const string SlotIdPrefix = "ring";

    /// <summary>
    ///     Number of rings that can be attached to these gloves.
    /// </summary>
    [DataField]
    public int MaxRings = 1;

    /// <summary>
    ///     Template used to create each ring slot.
    /// </summary>
    [DataField]
    public ItemSlot RingSlot = new();

    public static string GetSlotId(int index)
    {
        return $"{SlotIdPrefix}{index}";
    }
}
