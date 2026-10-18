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

    /// <summary>
    /// Played while a lock is being acquired (a random pick from the collection).
    /// </summary>
    [DataField]
    public SoundSpecifier LockStartSound = new SoundCollectionSpecifier("ShipCombatLockProcessing");

    /// <summary>
    /// Played when a lock is refused (MASTER SAFE, GREEN or out of range).
    /// </summary>
    [DataField]
    public SoundSpecifier LockDeniedSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/denybeep1.ogg");

    /// <summary>
    /// Played when the ship's lock is cleared.
    /// </summary>
    [DataField]
    public SoundSpecifier LockClearedSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/computerbeep_35.ogg");

    /// <summary>
    /// Played on the consoles of a ship that is being locked.
    /// </summary>
    [DataField]
    public SoundSpecifier IncomingLockSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/consolewarning.ogg");

    /// <summary>
    /// How long a lock takes to acquire. Machine part ratings will scale this in a later milestone.
    /// </summary>
    [DataField]
    public TimeSpan LockTime = TimeSpan.FromSeconds(2);
}

[Serializable, NetSerializable]
public enum TacticalConsoleUiKey : byte
{
    Key,
}
