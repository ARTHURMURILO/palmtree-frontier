using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: the engineering console's power presets.
/// </summary>
[Serializable, NetSerializable]
public enum ShipPowerPreset : byte
{
    /// <summary>Weapons 100, Shields 50, Thrusters 25, Auxiliary 25.</summary>
    Attack,

    /// <summary>Shields 100, Weapons 50, Thrusters 25, Auxiliary 25.</summary>
    Defense,

    /// <summary>Every pool at its 50-point baseline.</summary>
    Balanced,
}
