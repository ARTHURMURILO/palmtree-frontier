using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: the ship targeting ("tactical") console. It shares the grid's
/// <see cref="ShipCombatComponent"/> with every other console and ship device, renders the IFF view
/// with tactical overlays and carries the fire controls.
/// </summary>
[RegisterComponent]
public sealed partial class ShipTargetingConsoleComponent : Component
{
    /// <summary>
    /// Played when a new grid enters the console's radar range.
    /// </summary>
    [DataField]
    public SoundSpecifier ContactEnterSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/computerbeep_71.ogg");

    /// <summary>
    /// Played when a grid leaves the console's radar range.
    /// </summary>
    [DataField]
    public SoundSpecifier ContactLeaveSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/computerbeep_72.ogg");

    /// <summary>
    /// Minimum time between two contact sounds.
    /// </summary>
    [DataField]
    public TimeSpan ContactSoundCooldown = TimeSpan.FromSeconds(2);
}

[Serializable, NetSerializable]
public enum TacticalConsoleUiKey : byte
{
    Key,
}
