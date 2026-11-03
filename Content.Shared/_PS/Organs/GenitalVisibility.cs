using Robust.Shared.Serialization;

namespace Content.Shared._PS.Organs;

/// <summary>
/// Per-organ exposure rule, ported from SPLURT/Sandstorm's genital visibility toggles.
/// Controls both interaction exposure checks and whether the render marking is drawn.
/// </summary>
[Serializable, NetSerializable]
public enum GenitalVisibility : byte
{
    /// <summary>Never exposed and never drawn ("Always hidden").</summary>
    AlwaysHidden = 0,

    /// <summary>Hidden by underwear and clothing ("Hidden by underwear").</summary>
    HiddenByUnderwear = 1,

    /// <summary>Hidden by jumpsuits and outer clothing ("Hidden by jumpsuit"). Default.</summary>
    HiddenByJumpsuit = 2,

    /// <summary>Always exposed and drawn over clothing ("Never hidden").</summary>
    NeverHidden = 3,

    /// <summary>
    /// Always exposed, but kept at the normal genital layer so clothing still draws over it
    /// ("Visible under clothes"). Unlike <see cref="NeverHidden"/> this never raises the sprite
    /// above outer clothing.
    /// </summary>
    VisibleUnderClothes = 4,
}
