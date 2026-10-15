using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// Asks the registrar to register the selected grid as a civilian vessel with the given name.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipRegistrarRegisterMessage(NetEntity grid, string ownerUserId, string name, string suffix)
    : BoundUserInterfaceMessage
{
    public NetEntity Grid { get; } = grid;

    /// <summary>Net user id of the player the ship is registered to.</summary>
    public string OwnerUserId { get; } = ownerUserId;

    public string Name { get; } = name;
    public string Suffix { get; } = suffix;
}

/// <summary>
/// Asks the registrar to open a live camera preview of the selected grid.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipRegistrarPreviewMessage(NetEntity grid) : BoundUserInterfaceMessage
{
    public NetEntity Grid { get; } = grid;
}
