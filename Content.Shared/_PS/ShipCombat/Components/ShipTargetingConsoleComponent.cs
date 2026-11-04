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

    /// <summary>
    /// Played on the ship's consoles when damage raises the condition out of peacetime automatically.
    /// </summary>
    [DataField]
    public SoundSpecifier ConditionRaisedSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/alert14.ogg");

    /// <summary>Played once when a torpedo is first detected inbound (under 800 tiles).</summary>
    [DataField]
    public SoundSpecifier MissileDetectedSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/alert14.ogg");

    /// <summary>Beeped while a torpedo is inbound from 800 to 200 tiles.</summary>
    [DataField]
    public SoundSpecifier MissileWarningSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/alert19.ogg");

    /// <summary>Beeped faster while a torpedo is inbound closer than 200 tiles.</summary>
    [DataField]
    public SoundSpecifier MissileCloseSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/alert18.ogg");

    /// <summary>Played every five seconds while the ship is armed but has no operational weapons.</summary>
    [DataField]
    public SoundSpecifier WeaponsCriticalSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/critical.ogg");

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
public enum TacticalConsoleUiKey : byte
{
    Key,
}
