using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: one of the ship's three power pools (STO-adapted budget).
/// </summary>
[Serializable, NetSerializable]
public enum ShipPowerPool : byte
{
    Shields,
    Weapons,
    Thrusters,
}
