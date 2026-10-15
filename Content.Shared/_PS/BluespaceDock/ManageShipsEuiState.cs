using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// State for the admin ship manager: player ships (deeds) in the world, by dropdown.
/// </summary>
[Serializable, NetSerializable]
public sealed class ManageShipsEuiState(List<ManageShipEntry> ships, string? status, bool statusIsError)
    : EuiStateBase
{
    public readonly List<ManageShipEntry> Ships = ships;
    public readonly string? Status = status;
    public readonly bool StatusIsError = statusIsError;
}

/// <summary>
/// One player ship in the world.
/// </summary>
[Serializable, NetSerializable]
public sealed class ManageShipEntry(NetEntity grid, string name, string owner, bool docked)
{
    public readonly NetEntity Grid = grid;
    public readonly string Name = name;
    public readonly string Owner = owner;
    public readonly bool Docked = docked;
}
