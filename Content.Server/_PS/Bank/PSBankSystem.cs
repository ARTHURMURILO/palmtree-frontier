using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server._NF.Bank;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._PS.Bank;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._PS.Bank;

/// <summary>
/// Palmtree: stable bank account numbers and the transfer primitive used by SpesoFlow Pay.
/// The number is derived from the player's user id and character slot; it is not stored in the DB,
/// so it survives round restarts but not deleting/recreating the character slot.
/// </summary>
public sealed class PSBankSystem : EntitySystem
{
    [Dependency] private readonly IdCardSystem _idCards = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly BankSystem _bank = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<BankAccountNumberComponent, ExaminedEvent>(OnExamined);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (TryComp<BankAccountComponent>(args.Mob, out var bank))
            EnsureAccountNumber((args.Mob, bank));
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (TryComp<BankAccountComponent>(args.Entity, out var bank))
            EnsureAccountNumber((args.Entity, bank));
    }

    private void OnExamined(Entity<BankAccountNumberComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.Number == 0)
            return;

        args.PushMarkup(Loc.GetString("bank-account-number-examine", ("number", ent.Comp.Number)));
    }

    /// <summary>
    /// Computes the account number for a user's character slot. The same user + slot always maps to
    /// the same number, and the 9-digit range keeps accidental collisions vanishingly unlikely.
    /// </summary>
    public static int ComputeAccountNumber(NetUserId userId, int slot)
    {
        var bytes = userId.UserId.ToByteArray();
        var value = BitConverter.ToUInt64(bytes, 0) ^ (ulong) (uint) slot;
        return (int) (value % 900_000_000UL) + 100_000_000;
    }

    /// <summary>
    /// Gets the account number for a spawned player, if their preferences are available.
    /// </summary>
    public bool TryGetAccountNumber(EntityUid mob, out int number)
    {
        number = 0;

        if (!_players.TryGetSessionByEntity(mob, out var session) ||
            !_preferences.TryGetCachedPreferences(session.UserId, out var prefs))
            return false;

        var slot = 0;
        if (prefs.SelectedCharacter is HumanoidCharacterProfile profile)
        {
            var index = prefs.IndexOfCharacter(profile);
            if (index != -1)
                slot = index;
        }

        number = ComputeAccountNumber(session.UserId, slot);
        return true;
    }

    /// <summary>
    /// Updates the account number on the mob and the ID card the mob is carrying.
    /// </summary>
    public void EnsureAccountNumber(Entity<BankAccountComponent> ent)
    {
        if (!TryGetAccountNumber(ent.Owner, out var number))
            return;

        if (ent.Comp.AccountNumber != number)
        {
            ent.Comp.AccountNumber = number;
            Dirty(ent);
        }

        // The ID card is usually inside the player's PDA, which may be in a pocket or backpack
        // rather than the "id" inventory slot, so scan PDAs owned by this mob as well.
        var pdaQuery = EntityQueryEnumerator<PdaComponent>();
        while (pdaQuery.MoveNext(out _, out var pda))
        {
            if (pda.PdaOwner != ent.Owner ||
                pda.ContainedId is not { } contained ||
                !TryComp<BankAccountNumberComponent>(contained, out var card))
                continue;

            if (card.Number != number)
            {
                card.Number = number;
                Dirty(contained, card);
            }
        }

        if (!_idCards.TryFindIdCard(ent.Owner, out var idCard) ||
            !TryComp<BankAccountNumberComponent>(idCard, out var directCard) ||
            directCard.Number == number)
            return;

        directCard.Number = number;
        Dirty(idCard, directCard);
    }

    /// <summary>
    /// Transfers spesos between two spawned players. Both ends must be online with loaded prefs,
    /// which is what Frontier's profile-backed bank requires.
    /// </summary>
    public bool TryTransfer(EntityUid sender, int targetAccount, int amount, out string error)
    {
        error = string.Empty;

        if (amount <= 0)
        {
            error = "invalid-amount";
            return false;
        }

        if (!TryComp<BankAccountComponent>(sender, out var senderBank))
        {
            error = "no-sender-account";
            return false;
        }

        if (senderBank.AccountNumber == targetAccount)
        {
            error = "self";
            return false;
        }

        EntityUid? recipient = null;
        var query = EntityQueryEnumerator<BankAccountComponent>();
        while (query.MoveNext(out var uid, out var bank))
        {
            if (uid == sender || bank.AccountNumber != targetAccount)
                continue;

            if (!_players.TryGetSessionByEntity(uid, out _))
                continue;

            recipient = uid;
            break;
        }

        if (recipient is not { } recipientUid)
        {
            error = "no-recipient";
            return false;
        }

        if (!_bank.TryGetBalance(sender, out var balance) || balance < amount)
        {
            error = "insufficient-funds";
            return false;
        }

        if (!_bank.TryBankWithdraw(sender, amount))
        {
            error = "withdraw-failed";
            return false;
        }

        if (!_bank.TryBankDeposit(recipientUid, amount))
        {
            // Refund the sender if the recipient's profile cannot be updated.
            _bank.TryBankDeposit(sender, amount);
            error = "deposit-failed";
            return false;
        }

        _adminLogger.Add(LogType.ATMUsage,
            LogImpact.Low,
            $"{ToPrettyString(sender):actor} transferred {amount} spesos to account #{targetAccount} ({ToPrettyString(recipientUid)})");

        return true;
    }
}
