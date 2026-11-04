using System.Numerics;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Salvage.Fulton;
using Content.Shared.Standing;
using Content.Shared.Storage.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: fultons can extract crates (with their contents) and downed mobs, and keep a crate
/// sealed while the extraction is pending.
/// </summary>
[TestFixture]
public sealed class FultonTests
{
    [Test]
    public async Task FultonSealsCrateAndCarriesContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var testMap = await pair.CreateTestMap();

        EntityUid crate = default;
        EntityUid crowbar = default;
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            crate = entMan.SpawnEntity("CrateGenericSteel", testMap.GridCoords);
            crowbar = entMan.SpawnEntity("Crowbar", testMap.GridCoords);
            beacon = entMan.SpawnEntity("FultonBeacon", testMap.GridCoords.Offset(new Vector2(5f, 5f)));

            var storageSystem = server.System<EntityStorageSystem>();

            // Opening the crate spills its contents onto the floor; applying a fulton should
            // close it and vacuum the nearby contents back in.
            storageSystem.OpenStorage(crate);
            Assert.That(storageSystem.IsOpen(crate), Is.True);

            var fultoned = entMan.AddComponent<FultonedComponent>(crate);
            fultoned.Beacon = beacon;
            fultoned.NextFulton = timing.CurTime + TimeSpan.FromMinutes(1);
        });

        await server.WaitAssertion(() =>
        {
            var storageSystem = server.System<EntityStorageSystem>();
            var storage = entMan.GetComponent<EntityStorageComponent>(crate);

            Assert.That(storageSystem.IsOpen(crate), Is.False, "the fulton should close the crate");
            Assert.That(storage.Contents.Contains(crowbar), Is.True, "the crate should pick up nearby contents when it closes");
        });

        // A crate waiting on a fulton cannot be opened.
        await server.WaitAssertion(() =>
        {
            var storageSystem = server.System<EntityStorageSystem>();
            var user = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(1f, 0f)));

            Assert.That(storageSystem.TryOpenStorage(user, crate), Is.False);
            Assert.That(storageSystem.IsOpen(crate), Is.False);

            entMan.DeleteEntity(user);
        });

        // Advance the fulton so it launches and check that the contents travel with the crate.
        await server.WaitPost(() =>
        {
            var fultoned = entMan.GetComponent<FultonedComponent>(crate);
            fultoned.NextFulton = TimeSpan.Zero;
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var transformSystem = server.System<SharedTransformSystem>();
            var cratePosition = transformSystem.GetWorldPosition(crate);
            var beaconPosition = transformSystem.GetWorldPosition(beacon);

            Assert.That((cratePosition - beaconPosition).Length(), Is.LessThan(2.5f), "the crate should fly to the beacon");
            Assert.That(entMan.HasComponent<FultonedComponent>(crate), Is.False);

            var crowbarPosition = transformSystem.GetWorldPosition(crowbar);
            Assert.That((crowbarPosition - cratePosition).Length(), Is.LessThan(0.1f), "the contents should move with the crate");

            var storage = entMan.GetComponent<EntityStorageComponent>(crate);
            Assert.That(storage.Contents.Contains(crowbar), Is.True, "the contents should travel with the crate");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FultonTargetsDownedMobsOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        EntityUid user = default;
        EntityUid beacon = default;
        EntityUid fulton = default;
        EntityUid resting = default;
        EntityUid standing = default;
        EntityUid critical = default;
        EntityUid dead = default;

        await server.WaitPost(() =>
        {
            var coords = testMap.GridCoords;

            user = entMan.SpawnEntity("MobHuman", coords);
            beacon = entMan.SpawnEntity("FultonBeacon", coords.Offset(new Vector2(4f, 0f)));
            fulton = entMan.SpawnEntity("Fulton", coords);
            resting = entMan.SpawnEntity("MobHuman", coords.Offset(new Vector2(0.5f, 0f)));
            standing = entMan.SpawnEntity("MobHuman", coords.Offset(new Vector2(1f, 0f)));
            critical = entMan.SpawnEntity("MobHuman", coords.Offset(new Vector2(1.5f, 0f)));
            dead = entMan.SpawnEntity("MobHuman", coords.Offset(new Vector2(2f, 0f)));

            var hands = server.System<SharedHandsSystem>();
            Assert.That(hands.TryForcePickupAnyHand(user, fulton), Is.True);

            var fultonComponent = entMan.GetComponent<FultonComponent>(fulton);
            fultonComponent.Beacon = beacon;
            fultonComponent.ApplyFultonDuration = TimeSpan.Zero;

            server.System<StandingStateSystem>().Down(resting, playSound: false, force: true);
            server.System<MobStateSystem>().ChangeMobState(critical, MobState.Critical);
            server.System<MobStateSystem>().ChangeMobState(dead, MobState.Dead);
        });

        await server.WaitAssertion(() =>
        {
            ApplyFulton(entMan, fulton, user, resting);
            Assert.That(entMan.HasComponent<FultonedComponent>(resting), Is.True, "resting mobs should be fultonable");

            ApplyFulton(entMan, fulton, user, critical);
            Assert.That(entMan.HasComponent<FultonedComponent>(critical), Is.True, "critical mobs should be fultonable");

            ApplyFulton(entMan, fulton, user, dead);
            Assert.That(entMan.HasComponent<FultonedComponent>(dead), Is.True, "dead mobs should be fultonable");

            ApplyFulton(entMan, fulton, user, standing);
            Assert.That(entMan.HasComponent<FultonedComponent>(standing), Is.False, "standing mobs should not be fultonable");
        });

        await pair.CleanReturnAsync();
    }

    private static void ApplyFulton(IEntityManager entMan, EntityUid fulton, EntityUid user, EntityUid target)
    {
        var targetXform = entMan.GetComponent<TransformComponent>(target);
        entMan.EventBus.RaiseLocalEvent(fulton,
            new AfterInteractEvent(user, fulton, target, targetXform.Coordinates, true));
    }
}
