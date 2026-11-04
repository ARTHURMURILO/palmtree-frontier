namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: the Bluespace Power Relay. It is a grid consumer that draws the combined demand of the
/// ship's active power pools (shields, weapons, thrusters) and feeds those devices remotely. It has
/// no battery and never supplies the ship's grid.
/// </summary>
[RegisterComponent]
public sealed partial class BluespacePowerSinkComponent : Component
{
    /// <summary>
    /// Nominal rated draw, in watts. Shown in the examine text; the actual draw is the combined
    /// demand of the ship's active devices.
    /// </summary>
    [DataField]
    public float RatedThroughput = 60000f;

    /// <summary>
    /// True when this relay is the one drawing for its grid (runtime). Extra relays are kept as
    /// inactive backups.
    /// </summary>
    [ViewVariables]
    public bool Active = true;
}
