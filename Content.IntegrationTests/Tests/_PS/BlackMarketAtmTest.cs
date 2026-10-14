using System.Numerics;
using Content.Server._NF.SectorServices;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.StationRecords;
using Content.Shared.UserInterface;
using NUnit.Framework;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: regular ATMs refuse wanted criminals; black market ATMs serve everyone.
/// </summary>
[TestFixture]
public sealed class BlackMarketAtmTest
{
    [Test]
    public async Task RegularAtmsRefuseWantedCriminals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var meta = server.System<MetaDataSystem>();
            var records = server.System<StationRecordsSystem>();
            var sectorService = server.System<SectorServiceSystem>();
            var wanted = server.System<Content.Server._PS.Security.WantedStatusSystem>();

            // The shipped black market ATMs carry the flag; regular ones do not.
            foreach (var id in new[] { "ComputerBlackMarketBankATM", "ComputerWallmountBlackMarketBankATM" })
            {
                var atmProto = proto.Index<EntityPrototype>(id);
                var atmComp = (BankATMComponent) atmProto.Components["BankATM"].Component;
                Assert.That(atmComp.BlackMarket, Is.True, $"{id} should be a black market ATM.");
            }

            var regularProto = proto.Index<EntityPrototype>("ComputerBankATM");
            var regularComp = (BankATMComponent) regularProto.Components["BankATM"].Component;
            Assert.That(regularComp.BlackMarket, Is.False, "Regular ATMs are not black market ATMs.");

            // Sector-wide records need the sector service host.
            var host = entMan.SpawnEntity(null, testMap.GridCoords);
            entMan.EnsureComponent<StationSectorServiceHostComponent>(host);
            var service = sectorService.GetServiceEntity();
            Assert.That(service.IsValid(), Is.True);
            Assert.That(entMan.TryGetComponent<StationRecordsComponent>(service, out var serviceRecords), Is.True);

            var citizen = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            meta.SetEntityName(citizen, "Lawful Citizen");
            var idCard = entMan.SpawnEntity("PassengerIDCard", testMap.GridCoords);
            records.CreateGeneralRecord(service, idCard, "Lawful Citizen", 20, "Human", Gender.Male,
                "Contractor", null, null, HumanoidCharacterProfile.DefaultWithSpecies("Human"), serviceRecords);

            // Carry the ID so the wanted check resolves the character through their own record key.
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(citizen, idCard), Is.True);

            // Bare ATMs: the wanted gate is the only handler on them.
            var blackMarket = entMan.SpawnEntity(null, testMap.GridCoords);
            entMan.EnsureComponent<BankATMComponent>(blackMarket).BlackMarket = true;
            var regular = entMan.SpawnEntity(null, testMap.GridCoords);
            entMan.EnsureComponent<BankATMComponent>(regular);

            var attempt = new ActivatableUIOpenAttemptEvent(citizen);
            entMan.EventBus.RaiseLocalEvent(regular, attempt);
            Assert.That(attempt.Cancelled, Is.False, "Regular ATMs serve law-abiding citizens.");

            attempt = new ActivatableUIOpenAttemptEvent(citizen);
            entMan.EventBus.RaiseLocalEvent(blackMarket, attempt);
            Assert.That(attempt.Cancelled, Is.False, "Black market ATMs serve law-abiding citizens too.");

            Assert.That(wanted.TrySetWanted(citizen, "test"), Is.True);

            attempt = new ActivatableUIOpenAttemptEvent(citizen);
            entMan.EventBus.RaiseLocalEvent(regular, attempt);
            Assert.That(attempt.Cancelled, Is.True, "Regular ATMs must refuse wanted criminals.");

            attempt = new ActivatableUIOpenAttemptEvent(citizen);
            entMan.EventBus.RaiseLocalEvent(blackMarket, attempt);
            Assert.That(attempt.Cancelled, Is.False, "Black market ATMs serve wanted criminals.");

            entMan.DeleteEntity(citizen);
        });

        await pair.CleanReturnAsync();
    }
}
