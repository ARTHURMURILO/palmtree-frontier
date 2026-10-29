namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: splits a ship's generator output between its power pools (shields, weapons, thrusters)
/// and feeds whatever is left (the Auxiliary pool) back into the ship's power grid. The battery
/// buffers the difference; when it runs dry everything browns out proportionally.
/// </summary>
[RegisterComponent]
public sealed partial class BluespacePowerSinkComponent : Component
{
    /// <summary>
    /// The power budget the engineering console's sliders distribute, in watts.
    /// </summary>
    [DataField]
    public float RatedThroughput = 30000f;
}
