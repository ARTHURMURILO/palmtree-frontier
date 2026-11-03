using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: the engineering console's power presets.
/// </summary>
[Serializable, NetSerializable]
public enum ShipPowerPreset : byte
{
    /// <summary>Weapons 120, Shields 15, Thrusters 15.</summary>
    Attack,

    /// <summary>Shields 120, Weapons 15, Thrusters 15.</summary>
    Defense,

    /// <summary>Every pool at its 50-point baseline.</summary>
    Balanced,
}
