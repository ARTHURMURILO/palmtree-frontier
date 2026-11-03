namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a thruster fed by the ship's Bluespace Power Relay. Its thrust scales with the
/// Thrusters pool (one third at 50 points, five sixths at 125 points).
/// </summary>
[RegisterComponent]
public sealed partial class BluespaceThrusterComponent : Component
{
    /// <summary>
    /// The parts-scaled thrust before the power multiplier. Saved so a stored ship keeps its upgraded
    /// thrusters; captured from the thruster on MapInit and on parts refresh.
    /// </summary>
    [DataField]
    public float PartsThrust;
}
