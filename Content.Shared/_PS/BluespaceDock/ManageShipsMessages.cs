using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// Opens the live ship preview for a player ship.
/// </summary>
[Serializable, NetSerializable]
public sealed class ManageShipsPreviewMessage(NetEntity grid) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
}

/// <summary>
/// Deletes a player ship and clears any deed pointing at it.
/// </summary>
[Serializable, NetSerializable]
public sealed class ManageShipsDeleteMessage(NetEntity grid) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
}
