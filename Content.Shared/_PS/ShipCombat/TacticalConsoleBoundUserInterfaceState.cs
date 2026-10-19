using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: everything the tactical console window renders. The IFF view itself is the shuttle
/// radar control, so the nav state travels with the console-specific fields.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>The radar state (range, coordinates, angle, docks).</summary>
    public NavInterfaceState NavState { get; }

    /// <summary>The ship's condition.</summary>
    public ShipCondition Condition { get; }

    /// <summary>The selected weapon group.</summary>
    public ShipWeaponMode WeaponMode { get; }

    /// <summary>What the weapons aim at on the locked grid.</summary>
    public ShipTargetMode TargetMode { get; }

    public bool AutomaticCondition { get; }
    public bool AutomaticFire { get; }
    public bool AutomaticRetaliation { get; }

    /// <summary>True when the console is muted (no sounds).</summary>
    public bool Silent { get; }

    /// <summary>True while another ship is locking this one (LOCK: YES).</summary>
    public bool BeingLocked { get; }

    /// <summary>True while a torpedo is inbound (MSL: YES).</summary>
    public bool IncomingMissile { get; }

    /// <summary>True while this ship holds a target lock.</summary>
    public bool HasLock { get; }

    /// <summary>
    /// True while an armed turret is holding fire because the ship's own hull blocks its line of
    /// fire (the cvar ps.ship_combat.turrets_shoot_through_hull is off).
    /// </summary>
    public bool WeaponsBlocked { get; }

    /// <summary>The locked grid, if any (used to draw the lock reticle).</summary>
    public NetEntity? LockTarget { get; }

    /// <summary>
    /// The locked grid's display name, truncated, or "?" for unknown/hidden contacts. Null when the
    /// ship has no lock (the window shows TARGET: NONE).
    /// </summary>
    public string? TargetName { get; }

    /// <summary>
    /// Ships the console can see: their armed state and whether they are locking this ship. Drives
    /// the red threat IFF and the lock cursors.
    /// </summary>
    public List<TacticalContactState> Contacts { get; }

    public TacticalConsoleBoundUserInterfaceState(
        NavInterfaceState navState,
        ShipCondition condition,
        ShipWeaponMode weaponMode,
        ShipTargetMode targetMode,
        bool automaticCondition,
        bool automaticFire,
        bool automaticRetaliation,
        bool silent,
        bool beingLocked,
        bool incomingMissile,
        bool hasLock,
        bool weaponsBlocked,
        NetEntity? lockTarget,
        string? targetName,
        List<TacticalContactState> contacts)
    {
        NavState = navState;
        Condition = condition;
        WeaponMode = weaponMode;
        TargetMode = targetMode;
        AutomaticCondition = automaticCondition;
        AutomaticFire = automaticFire;
        AutomaticRetaliation = automaticRetaliation;
        Silent = silent;
        BeingLocked = beingLocked;
        IncomingMissile = incomingMissile;
        HasLock = hasLock;
        WeaponsBlocked = weaponsBlocked;
        LockTarget = lockTarget;
        TargetName = targetName;
        Contacts = contacts;
    }
}

/// <summary>
/// Palmtree: one visible ship on the tactical console.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalContactState
{
    /// <summary>The contact's grid.</summary>
    public NetEntity Grid { get; set; }

    /// <summary>
    /// True when the contact is off MASTER SAFE and at YELLOW condition or above: its IFF renders
    /// red on the tactical console.
    /// </summary>
    public bool Armed { get; set; }

    /// <summary>True when this ship is currently locked by the contact (yellow lock cursor).</summary>
    public bool LockingUs { get; set; }

    public TacticalContactState()
    {
    }

    public TacticalContactState(NetEntity grid, bool armed, bool lockingUs)
    {
        Grid = grid;
        Armed = armed;
        LockingUs = lockingUs;
    }
}
