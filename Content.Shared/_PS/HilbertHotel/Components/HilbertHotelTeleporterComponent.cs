using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.HilbertHotel.Components;

/// <summary>
/// A check-in terminal that teleports players into a private, per-code hotel room.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HilbertHotelTeleporterComponent : Component
{
    /// <summary>
    /// Maximum number of rooms (active or reserved) this terminal will keep loaded.
    /// </summary>
    [DataField]
    public int MaxRooms = 32;

    /// <summary>
    /// Minimum time between two room creations from this terminal. Each creation loads a map, so
    /// this bounds delete/recreate cycling. Zero disables the cooldown.
    /// </summary>
    [DataField]
    public TimeSpan CreateCooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Prototype spawned inside rooms that do not already contain an exit door.
    /// </summary>
    [DataField]
    public EntProtoId ExitPrototype = "HilbertHotelExit";

    /// <summary>
    /// Prototype spawned inside rooms that do not already contain a room controller.
    /// </summary>
    [DataField]
    public EntProtoId ControllerPrototype = "HilbertHotelRoomController";
}
