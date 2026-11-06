using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: owns the per-grid <see cref="ShipCombatComponent"/>, the state every ship-combat console
/// and device shares (condition today; weapon mode, power distribution and shield state later).
/// </summary>
/// <remarks>
/// The component is never added by the grid itself: consoles and ship devices call
/// <see cref="TryEnsureShipCombat"/> on MapInit, and the bluespace dock's load-time registration
/// sweep calls it again so grids saved before/around this feature self-heal.
/// </remarks>
public sealed class ShipCombatSystem : EntitySystem
{
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("shipcombat");
    }

    /// <summary>
    /// Resolves the grid of <paramref name="entity"/> (or the entity itself when it is a grid) and
    /// ensures it has a <see cref="ShipCombatComponent"/>. Idempotent.
    /// </summary>
    public bool TryEnsureShipCombat(EntityUid entity, out Entity<ShipCombatComponent> combat)
    {
        combat = default;

        if (!TryResolveGrid(entity, out var grid))
            return false;

        if (!TryComp(grid, out ShipCombatComponent? component))
            component = AddComp<ShipCombatComponent>(grid);

        combat = (grid, component);
        return true;
    }

    /// <summary>
    /// Looks up the ship combat state for an entity without creating it.
    /// </summary>
    public bool TryGetShipCombat(EntityUid entity, out Entity<ShipCombatComponent> combat)
    {
        combat = default;

        if (!TryResolveGrid(entity, out var grid) || !TryComp(grid, out ShipCombatComponent? component))
            return false;

        combat = (grid, component);
        return true;
    }

    /// <summary>
    /// Changes the ship's condition and raises <see cref="ShipCombatConditionChangedEvent"/>.
    /// Returns false when the entity has no ship combat state.
    /// </summary>
    public bool TrySetCondition(EntityUid entity, ShipCondition condition)
    {
        if (!TryGetShipCombat(entity, out var combat))
            return false;

        SetCondition(combat, condition);
        return true;
    }

    /// <summary>
    /// Changes the ship's condition and broadcasts <see cref="ShipCombatConditionChangedEvent"/>.
    /// Setting the current condition again is a no-op.
    /// </summary>
    public void SetCondition(Entity<ShipCombatComponent> combat, ShipCondition condition)
    {
        var oldCondition = combat.Comp.Condition;
        if (oldCondition == condition)
            return;

        combat.Comp.Condition = condition;
        _sawmill.Info($"{ToPrettyString(combat.Owner)} condition {oldCondition} -> {condition}");
        var ev = new ShipCombatConditionChangedEvent(combat, oldCondition, condition);
        RaiseLocalEvent(ev);
    }

    /// <summary>
    /// The grid of an entity, or the entity itself when it is a grid. False when the entity is in space.
    /// </summary>
    public bool TryResolveGrid(EntityUid entity, out EntityUid grid)
    {
        grid = default;

        if (HasComp<MapGridComponent>(entity))
        {
            grid = entity;
            return true;
        }

        if (Transform(entity).GridUid is not { } gridUid)
            return false;

        grid = gridUid;
        return true;
    }
}
