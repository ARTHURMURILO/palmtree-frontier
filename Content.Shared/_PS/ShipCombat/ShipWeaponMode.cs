using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: which weapons the tactical console has selected.
/// </summary>
/// <remarks>
/// <see cref="MasterSafe"/> locks every weapon out. The other entries arm the weapons linked to the
/// matching device-link port (<c>Primary</c>, <c>Secondary</c>, <c>Aux</c>); <see cref="All"/> arms
/// every group. Torpedo launchers are never part of a group and are fired manually.
/// </remarks>
[Serializable, NetSerializable]
public enum ShipWeaponMode : byte
{
    /// <summary>Every weapon is safe.</summary>
    MasterSafe = 0,

    /// <summary>Every weapon group is armed.</summary>
    All = 1,

    /// <summary>Only weapons linked on the Primary port.</summary>
    Primary = 2,

    /// <summary>Only weapons linked on the Secondary port.</summary>
    Secondary = 3,

    /// <summary>Only weapons linked on the Aux port.</summary>
    Aux = 4,
}
