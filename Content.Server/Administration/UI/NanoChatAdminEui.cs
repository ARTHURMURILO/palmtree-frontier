using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Content.Shared.Access.Components;
using Content.Shared.Administration;
using Content.Shared._DV.CartridgeLoader.Cartridges;
using Content.Shared._DV.NanoChat;
using Content.Shared.Eui;
using Content.Shared.PDA;

namespace Content.Server.Administration.UI;

/// <summary>
/// Admin EUI for viewing all NanoChat messages between players
/// </summary>
public sealed class NanoChatAdminEui : BaseEui
{
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public NanoChatAdminEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        // Check if the player has admin permissions
        if (!_adminManager.HasAdminFlag(Player, AdminFlags.Admin))
        {
            return new NanoChatAdminEuiState();
        }

        var cards = new List<NanoChatCardData>();

        // Query all NanoChat cards in the game
        var query = _entityManager.EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var nanoChatCard))
        {
            // Get ID card info if available
            string ownerName = "Unknown";
            string? jobTitle = null;
            string? username = null;

            if (_entityManager.TryGetComponent<IdCardComponent>(uid, out var idCard))
            {
                ownerName = idCard.FullName ?? "Unknown";
                jobTitle = idCard.LocalizedJobTitle;
            }

            // Try to find the player who currently controls this card. Prefer the PDA's
            // recorded owner, then walk up the container chain (PDA -> pocket/backpack -> mob).
            username = FindPlayerUsername(nanoChatCard.PdaUid ?? uid);

            var cardData = new NanoChatCardData
            {
                CardEntity = _entityManager.GetNetEntity(uid),
                Number = nanoChatCard.Number,
                OwnerName = ownerName,
                Username = username,
                OriginalOwnerUsername = nanoChatCard.OriginalOwnerUsername,
                JobTitle = jobTitle,
                Recipients = new Dictionary<uint, NanoChatRecipient>(nanoChatCard.Recipients),
                Messages = new Dictionary<uint, List<NanoChatMessage>>()
            };

            // Deep copy messages to avoid modification issues
            foreach (var (recipientNumber, messageList) in nanoChatCard.Messages)
            {
                cardData.Messages[recipientNumber] = new List<NanoChatMessage>(messageList);
            }

            cards.Add(cardData);
        }

        // Sort cards by owner name for easier browsing
        cards.Sort((a, b) => string.Compare(a.OwnerName, b.OwnerName, StringComparison.OrdinalIgnoreCase));

        return new NanoChatAdminEuiState
        {
            Cards = cards
        };
    }

    private string? FindPlayerUsername(EntityUid uid)
    {
        if (_entityManager.TryGetComponent<PdaComponent>(uid, out var pda) &&
            pda.PdaOwner is { } owner &&
            _playerManager.TryGetSessionByEntity(owner, out var ownerSession))
            return ownerSession.Name;

        for (var i = 0; i < 10 && uid.IsValid(); i++)
        {
            if (_playerManager.TryGetSessionByEntity(uid, out var session))
                return session.Name;

            if (!_entityManager.TryGetComponent<TransformComponent>(uid, out var transform))
                return null;

            uid = transform.ParentUid;
        }

        return null;
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case NanoChatAdminEuiMsg.Refresh:
                if (!_adminManager.HasAdminFlag(Player, AdminFlags.Admin))
                {
                    Close();
                    break;
                }
                StateDirty();
                break;

            case NanoChatAdminEuiMsg.SelectCard selectCard:
                // Could be used for future functionality like highlighting or filtering
                break;
        }
    }
}
