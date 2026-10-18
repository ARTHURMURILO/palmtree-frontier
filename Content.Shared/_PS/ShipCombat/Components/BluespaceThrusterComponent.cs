namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a thruster fed by the ship's bluespace power sink. Its thrust scales with the Thrusters
/// pool (0.65x at the minimum to 1.75x at the maximum).
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
