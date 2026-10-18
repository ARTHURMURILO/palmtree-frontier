using Robust.Shared.GameObjects;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: broadcast when a grid's <see cref="Components.ShipCombatComponent.Condition"/> changes.
/// </summary>
/// <remarks>
/// Broadcast rather than directed at the grid: Robust allows a single component-event subscription
/// per (component, event) pair, and several systems (the consoles, the alert system, the condition
/// sirens) react to a condition change. Filter on <see cref="Grid"/> in the handler.
/// </remarks>
public sealed class ShipCombatConditionChangedEvent : EntityEventArgs
{
    /// <summary>The grid whose condition changed.</summary>
    public EntityUid Grid { get; }

    /// <summary>The condition before the change.</summary>
    public ShipCondition OldCondition { get; }

    /// <summary>The condition after the change.</summary>
    public ShipCondition NewCondition { get; }

    public ShipCombatConditionChangedEvent(EntityUid grid, ShipCondition oldCondition, ShipCondition newCondition)
    {
        Grid = grid;
        OldCondition = oldCondition;
        NewCondition = newCondition;
    }
}
