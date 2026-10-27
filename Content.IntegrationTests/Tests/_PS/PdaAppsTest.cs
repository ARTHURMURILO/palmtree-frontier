using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Server._DV.CartridgeLoader.Cartridges;
using Content.Server._PS.CartridgeLoader.Cartridges;
using Content.Shared._DV.NanoChat;
using Content.Shared._NF.Bank.Components;
using Content.Shared._PS.Bank;
using Content.Shared.Hands.EntitySystems;
using Content.Server._PS.Bank;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: verifies the two new PDA apps are installed by default and that ID cards carry the
/// NanoChat number and bank account number components.
/// </summary>
[TestFixture]
public sealed class PdaAppsTest
{
    [Test]
    public async Task PdasInstallNanoChatAndSpesoFlowPay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var pda = entMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var programs = server.System<CartridgeLoaderSystem>().GetInstalled(pda);

            Assert.Multiple(() =>
            {
                Assert.That(programs.Any(uid => entMan.HasComponent<NanoChatCartridgeComponent>(uid)),
                    Is.True, "The NanoChat cartridge should be preinstalled on a passenger PDA.");
                Assert.That(programs.Any(uid => entMan.HasComponent<PSBankTransferCartridgeComponent>(uid)),
                    Is.True, "The SpesoFlow Pay cartridge should be preinstalled on a passenger PDA.");
            });

            var card = entMan.SpawnEntity("PassengerIDCard", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<NanoChatCardComponent>(card),
                    Is.True, "ID cards should carry a NanoChat card.");
                Assert.That(entMan.HasComponent<BankAccountNumberComponent>(card),
                    Is.True, "ID cards should carry a bank account number.");
                Assert.That(entMan.GetComponent<NanoChatCardComponent>(card).Number,
                    Is.Not.Null, "NanoChat numbers should be assigned on map init.");
            });

            entMan.DeleteEntity(card);
            entMan.DeleteEntity(pda);

            // A spawned player's bank account gets a stable 9-digit number and the ID card is
            // updated to match it.
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var bank = entMan.EnsureComponent<BankAccountComponent>(mob);
            var mobCard = entMan.SpawnEntity("PassengerIDCard", map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(mob, mobCard), Is.True);

            var playerManager = server.ResolveDependency<ISharedPlayerManager>();
            Assert.That(playerManager.SetAttachedEntity(pair.Player, mob), Is.True);

            var psBank = server.System<PSBankSystem>();
            Assert.That(bank.AccountNumber, Is.Not.Zero, "PlayerAttached should set the bank account number.");
            psBank.EnsureAccountNumber((mob, bank));

            Assert.Multiple(() =>
            {
                Assert.That(bank.AccountNumber, Is.InRange(100_000_000, 999_999_999));
                Assert.That(entMan.GetComponent<BankAccountNumberComponent>(mobCard).Number,
                    Is.EqualTo(bank.AccountNumber), "The ID card should show the same account number as the mob.");
                Assert.That(PSBankSystem.ComputeAccountNumber(pair.Player.UserId, 0), Is.EqualTo(bank.AccountNumber));
            });

            Assert.Multiple(() =>
            {
                Assert.That(psBank.TryTransfer(mob, bank.AccountNumber, 10, out var selfError), Is.False);
                Assert.That(selfError, Is.EqualTo("self"));
                Assert.That(psBank.TryTransfer(mob, 123456789, 10, out var missingError), Is.False);
                Assert.That(missingError, Is.EqualTo("no-recipient"));
                Assert.That(psBank.TryTransfer(mob, 123456789, 0, out var amountError), Is.False);
                Assert.That(amountError, Is.EqualTo("invalid-amount"));
            });

            playerManager.SetAttachedEntity(pair.Player, null);
            entMan.DeleteEntity(mobCard);
            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}
