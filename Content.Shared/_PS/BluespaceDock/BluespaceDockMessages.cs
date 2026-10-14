using Robust.Shared.Serialization;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// Asks the console to store the ship the inserted ID's deed points at.
/// </summary>
[Serializable, NetSerializable]
public sealed class BluespaceDockStoreMessage : BoundUserInterfaceMessage;

/// <summary>
/// Asks the console to pull a stored ship back out of the dock.
/// </summary>
[Serializable, NetSerializable]
public sealed class BluespaceDockRetrieveMessage(string shipId) : BoundUserInterfaceMessage
{
    public string ShipId { get; } = shipId;
}

/// <summary>
/// Sells a ship that is still stored in the dock (no ship is spawned).
/// </summary>
[Serializable, NetSerializable]
public sealed class BluespaceDockSellStoredMessage(string shipId) : BoundUserInterfaceMessage
{
    public string ShipId { get; } = shipId;
}

/// <summary>
/// Sells the active ship the inserted ID's deed points at, exactly like a shipyard console.
/// </summary>
[Serializable, NetSerializable]
public sealed class BluespaceDockSellActiveMessage : BoundUserInterfaceMessage;
