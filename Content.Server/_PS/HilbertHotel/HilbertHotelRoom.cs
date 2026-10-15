using Content.Shared._PS.HilbertHotel;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._PS.HilbertHotel;

/// <summary>
/// Server-side state for one reserved hotel room.
/// </summary>
public sealed class HilbertHotelRoom
{
    /// <summary>
    /// The shareable code players enter at a check-in terminal.
    /// </summary>
    public int Code;

    /// <summary>
    /// The terminal this room was checked in at. The room limit is per terminal, so this is what
    /// <c>MaxRooms</c> counts.
    /// </summary>
    public EntityUid Origin;

    /// <summary>
    /// Map holding the room. The map is deleted when the reservation expires.
    /// </summary>
    public MapId MapId;

    /// <summary>
    /// Map entity of the room map.
    /// </summary>
    public EntityUid MapUid;

    /// <summary>
    /// Prototype ID of the archetype this room was created from.
    /// </summary>
    public string TemplateId = string.Empty;

    /// <summary>
    /// Display name of the archetype, captured at creation time.
    /// </summary>
    public string Name = string.Empty;

    /// <summary>
    /// Session that created the room. Only they may lock it or manage guests.
    /// </summary>
    public NetUserId? OwnerId;

    /// <summary>
    /// Last known character name of the owner, for UI display.
    /// </summary>
    public string OwnerName = string.Empty;

    /// <summary>
    /// Who may join the room with its code.
    /// </summary>
    public HilbertHotelRoomStatus Status = HilbertHotelRoomStatus.Open;

    /// <summary>
    /// Whether the room is listed on check-in terminals for players who are not the owner.
    /// </summary>
    public bool Visible = true;

    /// <summary>
    /// Players the owner has trusted while the room is set to guests-only.
    /// </summary>
    public readonly Dictionary<NetUserId, string> TrustedGuests = new();

    /// <summary>
    /// Where each player was before they checked in, so they can be sent back on exit.
    /// </summary>
    public readonly Dictionary<NetUserId, MapCoordinates> EntryPoints = new();

    /// <summary>
    /// Where players are sent if their own entry point is unavailable.
    /// </summary>
    public MapCoordinates DefaultReturn;

    /// <summary>
    /// When the room was first observed empty. Null while occupied.
    /// </summary>
    public TimeSpan? EmptySince;

    /// <summary>
    /// Where arriving players are placed inside the room.
    /// </summary>
    public MapCoordinates Landing;
}
