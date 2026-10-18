using Robust.Shared.GameObjects;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: raised on a grid when its <see cref="Components.ShipCombatComponent.Condition"/> changes.
/// </summary>
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
