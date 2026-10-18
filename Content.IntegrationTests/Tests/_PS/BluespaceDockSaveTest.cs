using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server.Botany.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Climbing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Shuttles.Components;
using Content.Shared.Storage;
using NUnit.Framework;
using Robust.Server.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the bluespace dock's ship files must survive the things players keep on their ships. A
/// live save serializes every component on the grid, so any component holding a field the serializer
/// cannot write failed the whole save - a used plant analyzer (the "last scanned plant" memory) and
/// an in-progress do-after both used to do exactly that.
/// </summary>
[TestFixture]
public sealed class BluespaceDockSaveTest
{
    /// <summary>
    /// Components whose fields cannot be serialized break the save before any file is written, and
    /// the dock cannot store the ship at all.
    /// </summary>
    [Test]
    public async Task GridsWithUsedPlantAnalyzersAndDoAftersStillSave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        var analyzerName = string.Empty;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            // A plant analyzer that has scanned something: PlantInfo is set and the display would
            // otherwise be saved with it.
            var analyzer = entMan.SpawnEntity("PlantAnalyzerEmpty", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            analyzerName = entMan.GetComponent<MetaDataComponent>(analyzer).EntityName;
            var analyzerComp = entMan.GetComponent<PlantAnalyzerComponent>(analyzer);
            analyzerComp.PlantInfo = new()
            {
                TargetEntity = entMan.GetNetEntity(analyzer),
                IsTray = false,
                SeedName = "apples",
                Endurance = 100f,
                Lifespan = 55f,
                Maturation = 4f,
                Production = 6f,
                GrowthStages = 5,
                SeedPotency = 50f,
            };

            // An entity in the middle of a do-after (a climber, a grave being dug, a scan in
            // progress, ...) carries a DoAfterId. (The test map's grid is a single tile, so it has
            // to be spawned on it.)
            var climber = entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var climbing = entMan.EnsureComponent<ClimbingComponent>(climber);
            climbing.DoAfter = new DoAfterId(climber, 1);

            var path = BluespaceDockSystem.GetShipFilePath("plant-analyzer-save-test");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True,
                $"Saving a grid with a used plant analyzer failed: {error}");

            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;

            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);

            EntityUid? loadedAnalyzer = null;
            EntityUid? loadedClimber = null;
            foreach (var uid in entities)
            {
                switch (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                {
                    case "PlantAnalyzerEmpty":
                        loadedAnalyzer = uid;
                        break;
                    case "ToolboxMechanicalFilled":
                        loadedClimber = uid;
                        break;
                }
            }

            Assert.That(loadedAnalyzer, Is.Not.Null,
                $"The loaded ship lost its plant analyzer ({analyzerName}).");

            // The scan memory is runtime UI state: it is not saved, and certainly not a failure.
            Assert.That(entMan.GetComponent<PlantAnalyzerComponent>(loadedAnalyzer!.Value).PlantInfo, Is.Null,
                "The plant analyzer's runtime scan memory should not survive a save.");

            // The in-progress do-after id survives as a normal field: the uid is remapped to the
            // loaded entity (a dangling one must not make the save or the load fail).
            Assert.That(loadedClimber, Is.Not.Null, "The loaded ship lost the climbing toolbox.");
            var loadedClimbing = entMan.GetComponent<ClimbingComponent>(loadedClimber!.Value);
            Assert.That(loadedClimbing.DoAfter, Is.Not.Null, "The do-after id was not read back.");
            Assert.That(loadedClimbing.DoAfter!.Value.Uid, Is.EqualTo(loadedClimber.Value),
                "The do-after id was not remapped to the loaded entity.");
            Assert.That(loadedClimbing.DoAfter.Value.Index, Is.EqualTo(1), "The do-after id's index changed.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An update that removes a prototype **inside a container** on a stored ship: the sanitized
    /// copy drops the entity (and its contents), and the containers that named it must not be left
    /// pointing at a uid the copy no longer declares. The deserializer turns those into
    /// <c>EntityUid.Invalid</c> and logs "unknown entity yaml uid" for each one.
    /// </summary>
    [Test]
    public async Task MigratedShipFilesPruneReferencesToRemovedContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();
        var containers = server.System<ContainerSystem>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            // A toolbox holding one extra item, so the file has container references to the item.
            var toolbox = entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var duck = entMan.SpawnEntity("ToyRubberDuck", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            Assert.That(containers.TryGetContainer(toolbox, StorageComponent.ContainerId, out var storage), Is.True,
                "sanity: the toolbox has storage");
            Assert.That(containers.Insert(duck, storage!), Is.True, "sanity: the duck fits in the toolbox");
            var before = storage!.ContainedEntities.Count;

            var file = BluespaceDockSystem.GetShipFilePath("container-migration");
            Assert.That(dock.TrySaveShipFile(grid, file, out var saveError), Is.True, saveError);

            // Pretend the update removed the contained item's prototype.
            var path = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{file}");
            string text;
            using (var reader = res.UserData.OpenText(path))
            {
                text = reader.ReadToEnd();
            }

            Assert.That(text, Does.Contain("proto: ToyRubberDuck"),
                "sanity: the saved file names the contained item's prototype");

            using (var writer = res.UserData.OpenWriteText(path))
            {
                writer.Write(text.Replace("proto: ToyRubberDuck", "proto: PrototypeRemovedByAnUpdate"));
            }

            Assert.That(dock.TryLoadShipFile(file, out var loaded), Is.True,
                "A ship file whose contained item's prototype was removed must still load.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;

            // The toolbox survived without the duck, and nothing in it points at the dropped entity.
            EntityUid? loadedToolbox = null;
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);
            foreach (var uid in entities)
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ToolboxMechanicalFilled")
                    loadedToolbox = uid;
            }

            Assert.That(loadedToolbox, Is.Not.Null, "The toolbox was dropped with the removed prototype.");

            var loadedStorage = entMan.GetComponent<StorageComponent>(loadedToolbox!.Value);
            Assert.That(loadedStorage.StoredItems.Keys.Any(uid => !uid.IsValid()), Is.False,
                "The toolbox kept an invalid entity reference to the removed item.");

            Assert.That(containers.TryGetContainer(loadedToolbox.Value, StorageComponent.ContainerId, out var loadedContainer),
                Is.True);
            Assert.That(loadedContainer!.ContainedEntities.Any(uid => !uid.IsValid()), Is.False,
                "The toolbox's container kept an invalid entity reference to the removed item.");
            Assert.That(loadedContainer.ContainedEntities.Count, Is.EqualTo(before - 1),
                "Only the removed item should be gone from the toolbox.");

            dock.DeleteShipFile(file);
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship that is stored, deleted or sold must not leave radar consoles pointing at it: the
    /// console state refresh logs "Can't resolve MetaDataComponent" for a dead target on every
    /// refresh (the console's own ship arriving through FTL, for instance).
    /// </summary>
    [Test]
    public async Task RadarTargetsDoNotOutliveTheShipTheyPointAt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var radarSystem = server.System<RadarConsoleSystem>();
            var grid = testMap.Grid.Owner;

            // A shuttle console watching another grid.
            var console = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var ship = mapMan.CreateGridEntity(testMap.MapId).Owner;

            var radar = entMan.GetComponent<RadarConsoleComponent>(console);
            radarSystem.SetTarget((console, radar), entMan.GetNetEntity(ship), Vector2.Zero);
            Assert.That(radar.TargetEntity, Is.EqualTo(ship), "The console did not take the target.");

            // Deleting the watched ship (the admin ship manager and the dock's sell path) drops the
            // reference right away.
            Assert.That(dock.TryDeleteShip(ship, out var error), Is.True, error);
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(console).TargetEntity, Is.Null,
                "Deleting a ship left a radar console pointing at it.");

            // A target that was deleted without the console's knowledge (an older ship file, an
            // admin delete) is dropped when the console next refreshes its state.
            ship = mapMan.CreateGridEntity(testMap.MapId).Owner;
            radar = entMan.GetComponent<RadarConsoleComponent>(console);
            radarSystem.SetTarget((console, radar), entMan.GetNetEntity(ship), Vector2.Zero);
            Assert.That(radar.TargetEntity, Is.EqualTo(ship), "The console did not take the second target.");
            entMan.DeleteEntity(ship);

            server.System<ShuttleConsoleSystem>().RefreshShuttleConsoles();
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(console).TargetEntity, Is.Null,
                "A console refresh left a radar console pointing at a deleted ship.");
        });

        await pair.CleanReturnAsync();
    }
}
