using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: what the ship's weapons aim at on the locked grid. Subsystem modes make turrets shoot at
/// the matching components on the target instead of its centre.
/// </summary>
[Serializable, NetSerializable]
public enum ShipTargetMode : byte
{
    /// <summary>Aim at the target's centre: no subsystem preference.</summary>
    Random = 0,

    /// <summary>Aim at the target's hull (walls and other anchored structure).</summary>
    Hull = 1,

    /// <summary>Aim at the target's power generators.</summary>
    Generators = 2,

    /// <summary>Aim at the target's weapons.</summary>
    Weapons = 3,

    /// <summary>Aim at the target's thrusters.</summary>
    Thrusters = 4,
}
