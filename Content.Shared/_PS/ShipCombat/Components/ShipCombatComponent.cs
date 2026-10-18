namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: per-grid ship combat state, shared by every console and ship-combat device on the grid.
/// </summary>
/// <remarks>
/// The component is ensured by any ship-combat device on the grid, not by the grid itself. Everything
/// that points at another entity (lock targets, contacts, incoming torpedoes) is deliberately a
/// runtime-only field (no <c>[DataField]</c>) so the bluespace dock save/load drops it instead of
/// writing a dangling reference into the ship file.
/// </remarks>
[RegisterComponent]
public sealed partial class ShipCombatComponent : Component
{
    /// <summary>
    /// The ship's combat readiness. See <see cref="ShipCondition"/>.
    /// </summary>
    [DataField]
    public ShipCondition Condition = ShipCondition.Green;

    /// <summary>
    /// When true, taking damage while GREEN automatically raises the condition to YELLOW.
    /// </summary>
    [DataField]
    public bool AutomaticCondition = true;

    /// <summary>
    /// Which weapon group the tactical console has selected.
    /// </summary>
    [DataField]
    public ShipWeaponMode WeaponMode = ShipWeaponMode.MasterSafe;

    /// <summary>
    /// When true, weapons in the selected group fire automatically at the locked target.
    /// </summary>
    [DataField]
    public bool AutomaticFire;

    /// <summary>
    /// When true, being attacked locks the attacker and arms every weapon group automatically.
    /// </summary>
    [DataField]
    public bool AutomaticRetaliation;

    // Runtime-only lock state. These are deliberately not [DataField]s: the bluespace dock drops
    // them with the save, so a retrieved ship never carries a stale lock reference.

    /// <summary>The grid this ship is locking or has locked, if any.</summary>
    public EntityUid? LockTarget;

    /// <summary>True while a lock is being acquired.</summary>
    public bool LockInProgress;

    /// <summary>When the in-progress lock completes.</summary>
    public TimeSpan LockCompleteTime;

    /// <summary>Maximum distance to the lock target (0 = unlimited).</summary>
    public float LockRange;

    /// <summary>Grids currently locking this ship.</summary>
    public readonly HashSet<EntityUid> IncomingLocks = new();
}
