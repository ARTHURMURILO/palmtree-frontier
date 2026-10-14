using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.SectorServices;
using Content.Server._PS.Security;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._PS.Security;
using Content.Shared.CriminalRecords;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using NUnit.Framework;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the NFSD vault flags trespassers as wanted (and leaves NFSD/Command alone).
/// </summary>
[TestFixture]
public sealed class NfsdVaultTrespassTest
{
    [Test]
    public async Task TrespassersBecomeWanted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid contractorWithId = default;
        EntityUid contractorWithoutId = default;
        EntityUid deputy = default;

        await server.WaitAssertion(() =>
        {
            var hands = server.System<SharedHandsSystem>();
            var meta = server.System<MetaDataSystem>();
            var records = server.System<StationRecordsSystem>();
            var sectorService = server.System<SectorServiceSystem>();
            var wanted = server.System<WantedStatusSystem>();

            // Bring up the sector service host so the sector-wide records exist (normally the
            // station POI does this at round start).
            var host = entMan.SpawnEntity(null, testMap.GridCoords);
            entMan.EnsureComponent<StationSectorServiceHostComponent>(host);
            var service = sectorService.GetServiceEntity();
            Assert.That(service.IsValid(), Is.True, "The sector service entity should exist.");
            Assert.That(entMan.TryGetComponent<StationRecordsComponent>(service, out var serviceRecords), Is.True);

            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");

            contractorWithId = CreateRecordedMob(entMan, meta, records, service, serviceRecords, testMap,
                profile, "Contractor", "Wanted Walker", out var idCard);
            Assert.That(hands.TryPickupAnyHand(contractorWithId, idCard), Is.True);

            contractorWithoutId = CreateRecordedMob(entMan, meta, records, service, serviceRecords, testMap,
                profile, "Contractor", "Nameless Wanderer", out _);

            deputy = CreateRecordedMob(entMan, meta, records, service, serviceRecords, testMap,
                profile, "Deputy", "Lawful Deputy", out _);

            // The test grid plays the NFSD vault.
            entMan.EnsureComponent<NfsdVaultTrespassComponent>(testMap.Grid.Owner);

            Assert.That(wanted.IsWanted(contractorWithId), Is.False);
            Assert.That(wanted.IsWanted(contractorWithoutId), Is.False);
            Assert.That(wanted.IsWanted(deputy), Is.False);
        });

        // The sweep runs every half second.
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var wanted = server.System<WantedStatusSystem>();
            var records = server.System<StationRecordsSystem>();
            var sectorService = server.System<SectorServiceSystem>();
            var service = sectorService.GetServiceEntity();

            Assert.That(wanted.IsWanted(contractorWithId), Is.True,
                "A non-NFSD character on the vault should be flagged as wanted.");
            Assert.That(wanted.IsWanted(contractorWithoutId), Is.True,
                "Trespassers are flagged even without their ID card.");
            Assert.That(wanted.IsWanted(deputy), Is.False,
                "NFSD officers may enter their own vault.");

            // The reason is written onto the criminal record too.
            if (records.GetRecordByName(service, "Wanted Walker") is { } id &&
                records.TryGetRecord<CriminalRecord>(new StationRecordKey(id, service), out var record))
            {
                Assert.That(record.Status, Is.EqualTo(SecurityStatus.Wanted));
                Assert.That(record.Reason, Is.EqualTo(Loc.GetString("nfsd-vault-trespass-reason")));
            }
            else
            {
                Assert.Fail("Expected a criminal record for the trespasser.");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid CreateRecordedMob(
        IEntityManager entMan,
        MetaDataSystem meta,
        StationRecordsSystem records,
        EntityUid service,
        StationRecordsComponent serviceRecords,
        TestMapData testMap,
        HumanoidCharacterProfile profile,
        string job,
        string name,
        out EntityUid idCard)
    {
        var mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
        meta.SetEntityName(mob, name);

        idCard = entMan.SpawnEntity("PassengerIDCard", testMap.GridCoords);

        records.CreateGeneralRecord(service, idCard, name, 20, "Human", Gender.Male, job, null, null,
            profile, serviceRecords);

        return mob;
    }
}
