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
}
