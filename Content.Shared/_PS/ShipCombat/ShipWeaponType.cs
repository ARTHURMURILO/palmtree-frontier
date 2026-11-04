using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: how a ship weapon is aimed.
/// </summary>
[Serializable, NetSerializable]
public enum ShipWeaponType : byte
{
    /// <summary>Rotates within a firing arc around the angle it was placed at.</summary>
    Turret,

    /// <summary>Fixed: fires straight along its facing.</summary>
    Fixed,
}
