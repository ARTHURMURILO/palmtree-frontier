using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// Loads an orphaned ship file onto a paused map and opens the ship preview for it.
/// </summary>
[Serializable, NetSerializable]
public sealed class RestoreOrphanShipPreviewMessage(string file) : EuiMessageBase
{
    public readonly string File = file;
}

/// <summary>
/// Restores an orphaned ship file into a character's dock.
/// </summary>
[Serializable, NetSerializable]
public sealed class RestoreOrphanShipRestoreMessage(
    string file,
    string ownerUserId,
    string shipName,
    string suffix)
    : EuiMessageBase
{
    public readonly string File = file;

    /// <summary>Net user id of the player whose dock the ship goes into.</summary>
    public readonly string OwnerUserId = ownerUserId;

    public readonly string ShipName = shipName;
    public readonly string Suffix = suffix;
}

/// <summary>
/// Deletes an orphaned ship file.
/// </summary>
[Serializable, NetSerializable]
public sealed class RestoreOrphanShipDeleteMessage(string file) : EuiMessageBase
{
    public readonly string File = file;
}
