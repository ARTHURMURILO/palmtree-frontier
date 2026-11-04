namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a powered light that switches itself off while the grid's condition is
/// <see cref="OffCondition"/> (RED by default) and comes back on at any other condition, so the ship
/// goes dark at battle stations.
/// </summary>
[RegisterComponent]
public sealed partial class ShipConditionLightComponent : Component
{
    /// <summary>The condition that switches the light off.</summary>
    [DataField]
    public ShipCondition OffCondition = ShipCondition.Red;
}
