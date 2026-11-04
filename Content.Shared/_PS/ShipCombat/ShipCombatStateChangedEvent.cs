using Robust.Shared.GameObjects;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: broadcast when a grid's ship combat state other than the condition changes (locks,
/// weapon mode, power distribution, ...). Filter on <see cref="Grid"/>.
/// </summary>
/// <remarks>
/// Broadcast rather than directed: Robust allows a single component-event subscription per
/// (component, event) pair, and several systems react to state changes.
/// </remarks>
public sealed class ShipCombatStateChangedEvent : EntityEventArgs
{
    /// <summary>The grid whose state changed.</summary>
    public EntityUid Grid { get; }

    public ShipCombatStateChangedEvent(EntityUid grid)
    {
        Grid = grid;
    }
}
