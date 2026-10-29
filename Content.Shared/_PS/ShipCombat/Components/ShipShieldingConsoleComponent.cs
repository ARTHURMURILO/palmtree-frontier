using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: the engineering shielding console. Shares the grid's <see cref="ShipCombatComponent"/>
/// with the tactical console and controls the ship's power pools and shield distribution.
/// </summary>
[RegisterComponent]
public sealed partial class ShipShieldingConsoleComponent : Component
{
    /// <summary>Played when a shield facing collapses (at most every two seconds).</summary>
    [DataField]
    public SoundSpecifier FacingCollapseSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/damagealarm2.ogg");

    /// <summary>Played once when every shield facing is down.</summary>
    [DataField]
    public SoundSpecifier AllShieldsDownSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/damagealarm.ogg");

    /// <summary>Played when a collapsed facing comes back up.</summary>
    [DataField]
    public SoundSpecifier ShieldRestoredSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/input_ok_1_clean.ogg");

    /// <summary>
    /// When true the console plays no sounds (the Silent checkbox at the bottom of the window).
    /// </summary>
    [DataField]
    public bool Silent;

    /// <summary>True while the console has no power (runtime). Unpowered consoles are silent.</summary>
    public bool Unpowered;

    /// <summary>True when the console must play no sounds at all.</summary>
    public bool Muted => Silent || Unpowered;
}

[Serializable, NetSerializable]
public enum ShieldingConsoleUiKey : byte
{
    Key,
}
