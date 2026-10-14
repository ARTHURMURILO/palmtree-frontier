using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// State for the admin tool that lists orphaned ship files (files with no dock entry) and can
/// preview, restore or delete them.
/// </summary>
[Serializable, NetSerializable]
public sealed class RestoreOrphanShipEuiState(
    List<OrphanShipEntry> ships,
    List<OrphanShipPlayer> players,
    string? status,
    bool statusIsError)
    : EuiStateBase
{
    public readonly List<OrphanShipEntry> Ships = ships;
    public readonly List<OrphanShipPlayer> Players = players;
    public readonly string? Status = status;
    public readonly bool StatusIsError = statusIsError;
}

/// <summary>
/// One orphaned ship file.
/// </summary>
[Serializable, NetSerializable]
public sealed class OrphanShipEntry(string name, string modified)
{
    public readonly string Name = name;
    public readonly string Modified = modified;
}

/// <summary>
/// A player the ship can be restored to.
/// </summary>
[Serializable, NetSerializable]
public sealed class OrphanShipPlayer(string userId, string characterName)
{
    public readonly string UserId = userId;
    public readonly string CharacterName = characterName;
}
