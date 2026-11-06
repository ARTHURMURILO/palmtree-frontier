using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: a ship's combat readiness, stored on the grid's <see cref="Components.ShipCombatComponent"/>.
/// </summary>
/// <remarks>
/// GREEN is peacetime: every fire control on the ship is locked out. YELLOW arms the ship and RED is
/// battle stations (condition sirens). Changing the condition manually requires confirmation on the
/// console; the automatic condition can raise GREEN to YELLOW when the ship takes damage.
/// </remarks>
[Serializable, NetSerializable]
public enum ShipCondition : byte
{
    /// <summary>Peacetime: fire controls are locked out.</summary>
    Green = 0,

    /// <summary>The ship is armed.</summary>
    Yellow = 1,

    /// <summary>Battle stations.</summary>
    Red = 2,
}
