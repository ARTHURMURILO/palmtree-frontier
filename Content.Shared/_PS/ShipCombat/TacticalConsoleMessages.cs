using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: asks the tactical console to change the ship's condition (confirmed client-side).
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleSetConditionMessage(ShipCondition condition) : BoundUserInterfaceMessage
{
    public ShipCondition Condition { get; } = condition;
}

/// <summary>
/// Palmtree: toggles the automatic condition (damage raises GREEN to YELLOW).
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleSetAutomaticConditionMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

/// <summary>
/// Palmtree: toggles automatic fire at the locked target.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleSetAutomaticFireMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

/// <summary>
/// Palmtree: toggles automatic retaliation against the attacker.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleSetAutomaticRetaliationMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

/// <summary>
/// Palmtree: selects the armed weapon group.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleSetWeaponModeMessage(ShipWeaponMode mode) : BoundUserInterfaceMessage
{
    public ShipWeaponMode Mode { get; } = mode;
}

/// <summary>
/// Palmtree: asks the console to acquire a lock on the given grid.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleLockRequestMessage(NetEntity target) : BoundUserInterfaceMessage
{
    public NetEntity Target { get; } = target;
}

/// <summary>
/// Palmtree: clears the ship's lock.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalConsoleClearLockMessage : BoundUserInterfaceMessage;
