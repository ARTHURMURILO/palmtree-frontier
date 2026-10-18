using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: the engineering shielding console. Shares the grid's <see cref="ShipCombatComponent"/>
/// with the tactical console and controls the ship's power pools and shield distribution.
/// </summary>
[RegisterComponent]
public sealed partial class ShipShieldingConsoleComponent : Component
{
}

[Serializable, NetSerializable]
public enum ShieldingConsoleUiKey : byte
{
    Key,
}
