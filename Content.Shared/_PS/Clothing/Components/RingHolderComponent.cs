namespace Content.Shared._PS.Clothing;

/// <summary>
///     Allows rings to be stored in a pair of gloves. The ring slots are declared on the prototype's
///     <c>ItemSlots</c> component (see <c>ClothingHandsBase</c>) so that spawning a glove does not add
///     components at runtime.
/// </summary>
[RegisterComponent]
public sealed partial class RingHolderComponent : Component
{
    public const string SlotIdPrefix = "ring";

    /// <summary>
    ///     Number of rings that can be attached to these gloves. The prototype must declare a matching
    ///     item slot and container for each one, e.g. <c>ring0</c> for the default single slot.
    /// </summary>
    [DataField]
    public int MaxRings = 1;

    public static string GetSlotId(int index)
    {
        return $"{SlotIdPrefix}{index}";
    }
}
