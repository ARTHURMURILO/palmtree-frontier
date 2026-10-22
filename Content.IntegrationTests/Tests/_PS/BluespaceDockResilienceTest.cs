using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Interaction;
using Content.Shared.SmartFridge;
using Content.Shared.Station;
using NUnit.Framework;
using Robust.Server.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.UnitTesting;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

// Must be directly located in the namespace or the sourcegen can't find it.
/// <summary>
/// Test-only component with a field the serializer cannot write (the type has no data definition),
/// used to make a grid save fail exactly like a real runtime-only component does.
/// </summary>
[RegisterComponent]
public sealed partial class UnserializableTestComponent : Component
{
    [DataField]
    public UnserializableTestData? Data;
}

public sealed class UnserializableTestData
{
    public int Value = 42;
}

/// <summary>
/// Palmtree: the bluespace dock's safety nets. A ship is never refused a store over one entity that
/// cannot be serialized (it is skipped and re-created empty on retrieval), a failed store puts the
/// ship back exactly as it was (power included), and a used smart fridge no longer fails a save.
/// </summary>
[TestFixture]
public sealed class BluespaceDockResilienceTest
{
    [Test]
    public async Task UnserializableEntitiesAreReplacedWithFreshCopies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        // This test makes the first (strict) save attempt fail on purpose: the engine logs the
        // exception before the dock falls back to skipping the entity, which the test harness would
        // otherwise treat as a failure. Only this test raises the log failure level.
        await server.WaitPost(() =>
            server.ResolveDependency<IConfigurationManager>().SetCVar(RTCVars.FailureLogLevel, LogLevel.Fatal));

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            // A keeper and a machine that cannot be serialized (the test component has no data
            // definition), plus an item inside the keeper so the container reference has to be pruned.
            var toolbox = entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var duck = entMan.SpawnEntity("ToyRubberDuck", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var containers = server.System<ContainerSystem>();
            Assert.That(containers.TryGetContainer(toolbox, Content.Shared.Storage.StorageComponent.ContainerId, out var storage), Is.True);
            Assert.That(containers.Insert(duck, storage!), Is.True);

            var broken = entMan.SpawnEntity("APCBasic", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entMan.AddComponent<UnserializableTestComponent>(broken).Data = new UnserializableTestData();

            // And an item inside the toolbox that cannot be serialized: it has to be re-created inside
            // the same container on retrieval, not dropped or left on the floor.
            var brokenDuck = entMan.SpawnEntity("ToyRubberDuck", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entMan.AddComponent<UnserializableTestComponent>(brokenDuck).Data = new UnserializableTestData();
            Assert.That(containers.Insert(brokenDuck, storage!), Is.True);

            var file = BluespaceDockSystem.GetShipFilePath("resilient-save-test");
            Assert.That(dock.TrySaveShipFile(grid, file, out var error), Is.True,
                $"A ship with one unserializable entity must still be stored: {error}");

            // The dropped entity is recorded in the file, and the file still parses/loads.
            using (var reader = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>()
                       .UserData.OpenText(new Robust.Shared.Utility.ResPath($"{BluespaceDockSystem.ShipDirectory}/{file}")))
            {
                var text = reader.ReadToEnd();
                Assert.That(text, Does.Contain("droppedEntities"), "The skipped entity was not recorded in the ship file.");
                Assert.That(text, Does.Contain("proto: APCBasic"), "The skipped entity's prototype was not recorded.");
            }

            Assert.That(dock.TryLoadShipFile(file, out var loaded), Is.True, "Loading the ship file failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(file);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);

            var apcs = 0;
            var toolboxes = 0;
            foreach (var uid in entities)
            {
                switch (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                {
                    case "APCBasic":
                        apcs++;
                        Assert.That(entMan.HasComponent<UnserializableTestComponent>(uid), Is.False,
                            "The re-created machine still carries the unserializable test component.");
                        break;
                    case "ToolboxMechanicalFilled":
                        toolboxes++;
                        break;
                }
            }

            Assert.That(apcs, Is.EqualTo(1), "The machine that could not be serialized was not re-created.");
            Assert.That(toolboxes, Is.EqualTo(1), "The rest of the ship was not saved.");

            // The kept toolbox still has its contents, including a fresh copy of the item that could
            // not be serialized (re-inserted into the same container).
            var foundToolbox = entities.First(u => entMan.GetComponent<MetaDataComponent>(u).EntityPrototype?.ID == "ToolboxMechanicalFilled");
            var containers = server.System<ContainerSystem>();
            Assert.That(containers.TryGetContainer(foundToolbox, Content.Shared.Storage.StorageComponent.ContainerId, out var loadedStorage), Is.True);
            Assert.That(loadedStorage!.ContainedEntities.Count, Is.GreaterThan(0), "The toolbox lost its contents.");

            var ducks = loadedStorage.ContainedEntities.Count(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ToyRubberDuck");
            Assert.That(ducks, Is.EqualTo(1),
                "The item inside the toolbox that could not be serialized was not re-created inside it.");
            Assert.That(loadedStorage.ContainedEntities.All(uid =>
                    !entMan.HasComponent<UnserializableTestComponent>(uid)),
                "A re-created container item still carries the unserializable test component.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FailedStorePutsTheShipBackTogether()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid shipGrid = default;
        EntityUid computer = default;
        EntityUid shipStation = default;

        await server.WaitAssertion(() =>
        {
            var sharedDock = server.System<SharedBluespaceDockSystem>();
            var stationSystem = server.System<StationSystem>();
            var xformSystem = server.System<SharedTransformSystem>();
            var mapSystem = server.System<SharedMapSystem>();
            var stationGrid = testMap.Grid.Owner;

            stationSystem.InitializeNewStation(
                new StationConfig { StationPrototype = "StandardFrontierVessel" },
                new List<EntityUid> { stationGrid },
                "Resilience Station");

            // A powered ship: an APC with a charged battery and a console within its range.
            shipGrid = mapMan.CreateGridEntity(testMap.MapId).Owner;
            xformSystem.SetLocalPosition(shipGrid, new Vector2(50f, 0f));
            mapSystem.SetTiles(shipGrid, entMan.GetComponent<MapGridComponent>(shipGrid),
                new List<(Vector2i, Tile)>
                {
                    (new Vector2i(0, 0), new Tile(1)),
                    (new Vector2i(1, 0), new Tile(1)),
                });

            var apc = entMan.SpawnEntity("APCBasic", new EntityCoordinates(shipGrid, new Vector2(0.5f, 0.5f)));
            if (!entMan.GetComponent<TransformComponent>(apc).Anchored)
                xformSystem.AnchorEntity(apc);

            computer = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(shipGrid, new Vector2(1.5f, 0.5f)));
            if (!entMan.GetComponent<TransformComponent>(computer).Anchored)
                xformSystem.AnchorEntity(computer);

            shipStation = stationSystem.InitializeNewStation(
                new StationConfig { StationPrototype = "StandardFrontierVessel" },
                new List<EntityUid> { shipGrid },
                "Resilience Ship");

            sharedDock.AssignDeed(shipGrid, shipGrid, "Resilience Ship", null, "Owner");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var stationSystem = server.System<StationSystem>();

            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(computer).Powered, Is.True,
                "sanity: the console is powered before the store");

            // A store that fails after the ship has already been stripped and undocked (the tax
            // withdrawal is the last step before the ship is handed over). The grid has no bank
            // account, so the withdrawal fails.
            Assert.That(dock.TryStoreShip(shipGrid, "ckey-test", "Owner", value: 1000, payer: shipGrid, cost: 500,
                    out var error),
                Is.False, "The store was expected to fail on the tax withdrawal");
            Assert.That(error, Is.EqualTo("bluespace-dock-error-insufficient-funds"));

            // The failed store must leave the ship exactly as it was: same grid, deed, station
            // membership (its machinery needs the station) and power.
            Assert.That(entMan.EntityExists(shipGrid), Is.True, "The ship grid was deleted by a failed store.");
            Assert.That(dock.GetDeedShips().Any(d => d.Grid == shipGrid && d.Name.StartsWith("Resilience Ship")), Is.True,
                "The failed store did not restore the ship's deed.");
            Assert.That(stationSystem.GetOwningStation(shipGrid), Is.EqualTo(shipStation),
                "The failed store did not restore the ship's station membership.");
        });

        // A PowerChangedEvent from the restored link is processed on a later tick.
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(computer).Powered, Is.True,
                "The failed store left the ship's machinery un-powered.");
        });

        await pair.CleanReturnAsync();
    }
}
