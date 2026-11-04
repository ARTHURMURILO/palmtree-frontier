using System.Linq;
using Content.Server._DV.CartridgeLoader.Cartridges;
using Content.Server._DV.NanoChat;
using Content.Server.CartridgeLoader;
using Content.Server.PDA;
using Content.Shared._DV.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;
using Content.Shared._DV.NanoChat;
using Content.Shared.PDA;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: NanoChat delivery works globally (no station/telecomms requirement), so ships and
/// expeditions can message the sector.
/// </summary>
[TestFixture]
public sealed class NanoChatDeliveryTest
{
    [Test]
    public async Task MessagesDeliverWithoutStationOrTelecomms()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var cartridgeLoader = server.System<CartridgeLoaderSystem>();

        EntityUid senderPda = default;
        EntityUid senderCard = default;
        EntityUid receiverPda = default;
        EntityUid receiverCard = default;
        EntityUid senderMob = default;

        await server.WaitAssertion(() =>
        {
            senderPda = entMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var senderPdaComp = entMan.GetComponent<PdaComponent>(senderPda);
            Assert.That(senderPdaComp.ContainedId, Is.Not.Null, "PassengerPDA should start with an ID card.");
            senderCard = senderPdaComp.ContainedId!.Value;

            receiverPda = entMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var receiverPdaComp = entMan.GetComponent<PdaComponent>(receiverPda);
            Assert.That(receiverPdaComp.ContainedId, Is.Not.Null, "PassengerPDA should start with an ID card.");
            receiverCard = receiverPdaComp.ContainedId!.Value;

            senderMob = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        // Let NanoChatCartridgeSystem fill in the card references.
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var nanoChat = server.System<NanoChatSystem>();
            var senderChat = entMan.GetComponent<NanoChatCardComponent>(senderCard);

            var players = server.ResolveDependency<ISharedPlayerManager>();
            Assert.That(players.SetAttachedEntity(pair.Player, senderMob), Is.True);
            server.System<PdaSystem>().SetOwner(senderPda, entMan.GetComponent<PdaComponent>(senderPda), senderMob, pair.Player.Name);
            var receiverChat = entMan.GetComponent<NanoChatCardComponent>(receiverCard);
            var senderNumber = nanoChat.GetNumber((senderCard, senderChat));
            var receiverNumber = nanoChat.GetNumber((receiverCard, receiverChat));
            Assert.That(senderNumber, Is.Not.Null);
            Assert.That(receiverNumber, Is.Not.Null);

            var senderCartridge = cartridgeLoader.GetInstalled(senderPda)
                .Single(uid => entMan.HasComponent<NanoChatCartridgeComponent>(uid));
            var message = new NanoChatUiMessageEvent(
                NanoChatUiMessageType.SendMessage,
                receiverNumber,
                "hello from the void")
            {
                LoaderUid = entMan.GetNetEntity(senderPda),
            };

            entMan.EventBus.RaiseLocalEvent<CartridgeMessageEvent>(senderCartridge, message);

            var senderMessages = nanoChat.GetMessages((senderCard, senderChat));
            var messages = nanoChat.GetMessages((receiverCard, receiverChat));
            Assert.That(senderMessages.ContainsKey(receiverNumber!.Value), Is.True, "sender outbox should contain the message");
            Assert.That(messages.TryGetValue(senderNumber!.Value, out var chat), Is.True, "receiver inbox should contain the message");
            Assert.That(chat, Has.Count.EqualTo(1));
            Assert.That(chat![0].Content, Is.EqualTo("hello from the void"));
            Assert.That(chat[0].DeliveryFailed, Is.False);
            Assert.That(chat[0].SenderUsername, Is.EqualTo(pair.Player.Name));
            Assert.That(senderChat.OriginalOwnerUsername, Is.EqualTo(pair.Player.Name),
                "The first player to use the card becomes its original owner.");
        });

        await pair.CleanReturnAsync();
    }
}
