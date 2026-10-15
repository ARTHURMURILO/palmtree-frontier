using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// A player a ship can be handed to: their net user id plus the character name the ship consoles key
/// ownership on. Shared by the Ship Registrar's owner dropdown and the orphan restore tool.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipOwnerOption(string userId, string characterName)
{
    public readonly string UserId = userId;
    public readonly string CharacterName = characterName;
}
