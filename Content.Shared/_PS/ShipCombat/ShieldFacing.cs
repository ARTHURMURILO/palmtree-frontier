using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: one of a ship's four shield facings, in the grid's local frame (fore is +Y).
/// </summary>
[Serializable, NetSerializable]
public enum ShieldFacing : byte
{
    Fore,
    Starboard,
    Aft,
    Port,

    /// <summary>Not a facing: distributes shield energy evenly (the ALL button).</summary>
    All,
}
