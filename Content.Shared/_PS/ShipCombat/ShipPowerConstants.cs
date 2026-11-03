namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: the limits of the STO-adapted 150-point ship power budget.
/// </summary>
public static class ShipPowerConstants
{
    public const int PoolMin = 15;
    public const int PoolMax = 125;
    public const int PoolTotal = 150;

    /// <summary>Pools below this value cannot run their devices (the `(!)` alerts).</summary>
    public const int MinOperatingPool = 25;
}
