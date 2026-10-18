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

    public bool AutomaticCondition { get; }
    public bool AutomaticFire { get; }
    public bool AutomaticRetaliation { get; }

    /// <summary>True while another ship is locking this one (LOCK: YES).</summary>
    public bool BeingLocked { get; }

    /// <summary>True while a torpedo is inbound (MSL: YES).</summary>
    public bool IncomingMissile { get; }

    /// <summary>True while this ship holds a target lock.</summary>
    public bool HasLock { get; }

    public TacticalConsoleBoundUserInterfaceState(
        NavInterfaceState navState,
        ShipCondition condition,
        ShipWeaponMode weaponMode,
        bool automaticCondition,
        bool automaticFire,
        bool automaticRetaliation,
        bool beingLocked,
        bool incomingMissile,
        bool hasLock)
    {
        NavState = navState;
        Condition = condition;
        WeaponMode = weaponMode;
        AutomaticCondition = automaticCondition;
        AutomaticFire = automaticFire;
        AutomaticRetaliation = automaticRetaliation;
        BeingLocked = beingLocked;
        IncomingMissile = incomingMissile;
        HasLock = hasLock;
    }
}
