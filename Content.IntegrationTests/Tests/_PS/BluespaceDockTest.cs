using System.Collections.Generic;
using System.Linq;
using Content.Server._PS.BluespaceDock;
using Content.Server.Station.Systems;
using Content.Shared._NF.BindToStation;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._PS.BluespaceDock;
using Content.Shared._PS.ShipRegistry;
using Content.Shared._NF.Shipyard;
using Content.Shared.Station;
using NUnit.Framework;
using Content.Shared.Storage;
using Robust.Server.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the Bluespace Dock registry and its ship file round-trip (the risky part of the
/// feature - serializing a grid into UserData and loading it back), plus the self-healing sweep.
/// </summary>
[TestFixture]
public sealed class BluespaceDockTest
{
    [Test]
    public async Task RegistryRoundTripsThroughDisk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var res = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            dock.ReloadRegistry();
            dock.Registry.Entries.Clear();
            dock.SaveRegistry();

            // Entries need a real ship file or the self-heal sweep drops them.
            Assert.That(dock.TrySaveShipFile(testMap.Grid.Owner, BluespaceDockSystem.GetShipFilePath("testship"), out _), Is.True);

            dock.AddEntry(new BluespaceDockEntry
            {
                ShipId = "testship",
                File = BluespaceDockSystem.GetShipFilePath("testship"),
                OwnerUserId = "ckey-test",
                OwnerName = "Test Character",
                Model = "Dove",
                DisplayName = "Dove QT-723",
                Suffix = "QT-723",
                Value = 12345,
                StoredAt = "2026-01-01T00:00:00.0000000Z",
            });

            Assert.That(res.UserData.Exists(new ResPath(BluespaceDockSystem.RegistryPath)), Is.True,
                "The registry file should be written to user data.");

            // Reload from disk and check the entry survived.
            dock.ReloadRegistry();
            var entry = dock.Registry.Find("testship");
            Assert.That(entry, Is.Not.Null, "The registry entry should survive a reload.");
            Assert.That(entry!.OwnerName, Is.EqualTo("Test Character"));
            Assert.That(entry.Model, Is.EqualTo("Dove"));
            Assert.That(entry.Value, Is.EqualTo(12345));

            // Clean up so the test does not leak state into other tests.
            dock.Registry.Entries.RemoveAll(e => e.ShipId == "testship");
            dock.SaveRegistry();
            dock.DeleteShipFile(BluespaceDockSystem.GetShipFilePath("testship"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipFileRoundTrips()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();

            // Give the test grid some content worth preserving.
            var grid = testMap.Grid.Owner;
            var marker = entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new System.Numerics.Vector2(0.5f, 0.5f)));
            Assert.That(entMan.EntityExists(marker), Is.True);

            var path = BluespaceDockSystem.GetShipFilePath("roundtrip");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, $"Saving failed: {error}");

            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;

            // The loaded copy should be a real grid with the closet on it.
            Assert.That(entMan.HasComponent<MapGridComponent>(loadedGrid), Is.True);
            var children = entMan.GetComponent<TransformComponent>(loadedGrid).ChildEnumerator;
            var found = false;
            while (children.MoveNext(out var child))
            {
                if (entMan.HasComponent<Content.Shared.Storage.StorageComponent>(child))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "The saved ship should still contain its entities.");

            // Clean up: delete the ship file, the loaded grid and its map.
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(loadedGrid);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            server.System<SharedMapSystem>().DeleteMap(loadedMap);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipFilesCarryNoDeedOrStationReferences()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var stationSystem = server.System<StationSystem>();
            var sharedDock = server.System<SharedBluespaceDockSystem>();

            var grid = testMap.Grid.Owner;
            var station = stationSystem.InitializeNewStation(
                new StationConfig { StationPrototype = "StandardFrontierVessel" },
                new List<EntityUid> { grid },
                "Test Ship");

            // A station-bound object and a deed: both used to end up in the file, and the stale
            // station reference made the holopad rename throw while loading.
            var holopad = entMan.SpawnEntity("Holopad", new EntityCoordinates(grid, new System.Numerics.Vector2(0.5f, 0.5f)));
            if (!entMan.HasComponent<StationBoundObjectComponent>(holopad))
                entMan.AddComponent<StationBoundObjectComponent>(holopad);
            var bound = entMan.GetComponent<StationBoundObjectComponent>(holopad);
            bound.BoundStation = station;

            sharedDock.AssignDeed(grid, grid, "Dove", "QT-723", "Tester");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(grid), Is.True);

            Assert.That(dock.TrySaveShipFile(grid, "clean.yml", out var error), Is.True, $"Saving failed: {error}");

            string text;
            using (var reader = res.UserData.OpenText(new ResPath($"{BluespaceDockSystem.ShipDirectory}/clean.yml")))
            {
                text = reader.ReadToEnd();
            }

            Assert.That(text, Does.Not.Contain("ShuttleDeed"), "A ship file must not carry a deed.");
            Assert.That(text, Does.Not.Contain("StationMember"), "A ship file must not carry station membership.");
            Assert.That(text, Does.Not.Contain("StationBoundObject"), "A ship file must not carry station bindings.");

            // The file still loads, and loaded ships have no deed (the registry grants it back).
            Assert.That(dock.TryLoadShipFile("clean.yml", out var loaded), Is.True, "The cleaned file must load.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(loadedGrid), Is.False);
            Assert.That(entMan.HasComponent<Content.Server.Shuttles.Components.ShuttleComponent>(loadedGrid), Is.True,
                "A loaded ship must still be a shuttle (the shipyard refuses to sell ships without ShuttleComponent).");

            dock.DeleteShipFile("clean.yml");
        });

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(loadedGrid);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            server.System<SharedMapSystem>().DeleteMap(loadedMap);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CorruptShipFilesAreDetected()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var res = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();

            // A file written by the dock itself parses.
            Assert.That(dock.TrySaveShipFile(testMap.Grid.Owner, "parsecheck.yml", out _), Is.True);
            Assert.That(dock.IsShipFileParseable("parsecheck.yml"), Is.True);

            // Garbage does not, so its entry gets purged instead of failing forever.
            using (var writer = res.UserData.OpenWriteText(
                       new ResPath($"{BluespaceDockSystem.ShipDirectory}/corrupt.yml")))
            {
                writer.Write("this: [is not: valid yaml");
            }

            Assert.That(dock.IsShipFileParseable("corrupt.yml"), Is.False);
            Assert.That(dock.IsShipFileParseable("doesnotexist.yml"), Is.False);

            dock.DeleteShipFile("parsecheck.yml");
            dock.DeleteShipFile("corrupt.yml");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissingFilesArePurged()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            dock.ReloadRegistry();
            dock.Registry.Entries.Clear();
            dock.SaveRegistry();

            dock.AddEntry(new BluespaceDockEntry
            {
                ShipId = "ghostship",
                File = BluespaceDockSystem.GetShipFilePath("ghostship"),
                OwnerUserId = "ckey-test",
                OwnerName = "Test Character",
                DisplayName = "Ghost Ship",
            });

            Assert.That(dock.Registry.Find("ghostship"), Is.Not.Null);

            // The file does not exist, so the sweep must drop the entry (and persist that).
            dock.PurgeMissingEntries();
            Assert.That(dock.Registry.Find("ghostship"), Is.Null, "Entries without a ship file must be purged.");

            dock.ReloadRegistry();
            Assert.That(dock.Registry.Find("ghostship"), Is.Null, "The purge must be persisted.");

            dock.Registry.Entries.Clear();
            dock.SaveRegistry();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipNamesDoNotAccumulateSuffixes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            // The dock stores the base name and rebuilds the full name, so repeated store/retrieve
            // cycles cannot grow it (the reported "Dove NX-674 NX-647").
            Assert.That(ShipNameHelper.BuildFullName("Dove", "NX-674"), Is.EqualTo("Dove NX-674"));
            Assert.That(ShipNameHelper.BuildFullName("Dove NX-674", "NX-674"), Is.EqualTo("Dove NX-674"),
                "A name that already ends with the suffix must not get it twice.");
            Assert.That(ShipNameHelper.BuildFullName("Dove", null), Is.EqualTo("Dove"));

            Assert.That(ShipNameHelper.GetBaseName("Dove NX-674", "NX-674"), Is.EqualTo("Dove"));
            Assert.That(ShipNameHelper.GetBaseName("Dove NX-674 NX-647", "NX-647"), Is.EqualTo("Dove NX-674"),
                "Only one trailing suffix is stripped.");
            Assert.That(ShipNameHelper.GetBaseName("Dove", "NX-674"), Is.EqualTo("Dove"));
            Assert.That(ShipNameHelper.GetBaseName("Dove", null), Is.EqualTo("Dove"));

            // And the two are inverse for a well-formed name.
            var full = ShipNameHelper.BuildFullName("Dove", "NX-674");
            Assert.That(ShipNameHelper.GetBaseName(full, "NX-674"), Is.EqualTo("Dove"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StoredShipsAreOnDiskImmediatelyAndSurviveAReload()
    {
        // Storing writes the ship file and the registry entry to disk right away (nothing waits for
        // a round end) and only then deletes the original. Dropping the cache is exactly what a
        // restart does, so everything must come back from disk.
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();

            Assert.That(dock.TrySaveShipFile(testMap.Grid.Owner, BluespaceDockSystem.GetShipFilePath("survivor"), out _), Is.True);
            dock.AddEntry(new BluespaceDockEntry
            {
                ShipId = "survivor",
                File = BluespaceDockSystem.GetShipFilePath("survivor"),
                OwnerUserId = "ckey-test",
                OwnerName = "Test Character",
                Name = "Survivor",
                DisplayName = "Survivor QT-001",
                Suffix = "QT-001",
                Value = 4242,
            });

            // Both writes are already on disk before the original ship would be deleted.
            Assert.That(res.UserData.Exists(new ResPath(BluespaceDockSystem.RegistryPath)), Is.True,
                "The registry must be written at store time, not at round end.");
            Assert.That(res.UserData.Exists(new ResPath($"{BluespaceDockSystem.ShipDirectory}/survivor.yml")), Is.True,
                "The ship file must be written at store time, not at round end.");

            dock.ReloadRegistry();

            var entry = dock.Registry.Find("survivor");
            Assert.That(entry, Is.Not.Null, "A stored ship's registry entry must come back from disk.");
            Assert.That(entry!.DisplayName, Is.EqualTo("Survivor QT-001"));
            Assert.That(entry.Value, Is.EqualTo(4242));

            Assert.That(dock.TryLoadShipFile(entry.File, out var loaded), Is.True,
                "A stored ship's file must load back from disk.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;

            // Clean up so nothing leaks into other tests.
            dock.RemoveEntry(entry, deleteFile: true);
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrphanedShipFilesCanBeListedRestoredAndDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();
        var mapSystem = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            dock.ReloadRegistry();

            // A ship file with no registry entry is an orphan.
            var file = BluespaceDockSystem.GetShipFilePath("orphan");
            Assert.That(dock.TrySaveShipFile(testMap.Grid.Owner, file, out _), Is.True);
            Assert.That(dock.GetOrphanedFiles().Any(o => o.Name == file), Is.True,
                "A ship file with no entry must be listed as an orphan.");
            Assert.That(dock.GetOrphanedFiles().Any(o => o.Name == BluespaceDockSystem.RegistryFileName), Is.False,
                "The registry file is not an orphan.");
            Assert.That(dock.TryDeleteOrphanedFile(BluespaceDockSystem.RegistryFileName), Is.False,
                "The registry file must never be deletable through the orphan tool.");

            // Preview: loaded onto its own map, frozen so nothing moves while it is looked at.
            Assert.That(dock.TryLoadOrphanedFile(file, out var previewGrid, out var previewMap), Is.True);
            Assert.That(entMan.HasComponent<MapGridComponent>(previewGrid!.Value), Is.True);
            Assert.That(mapSystem.IsPaused(previewMap), Is.True, "A preview map must be paused.");

            entMan.QueueDeleteEntity(previewGrid!.Value);
            mapSystem.DeleteMap(previewMap);

            // Restore into a character's dock.
            Assert.That(
                dock.TryRestoreOrphanedFile(file, "ckey-orphan", "Test Character", "Dove", "QT-123", out var error),
                Is.True,
                $"Restore failed: {error}");

            var entry = dock.FindEntry("ckey-orphan", "Test Character", "Dove QT-123");
            Assert.That(entry, Is.Not.Null, "A restored ship must have a dock entry.");
            Assert.That(res.UserData.Exists(new ResPath($"{BluespaceDockSystem.ShipDirectory}/{entry!.File}")), Is.True,
                "The restored file must be back in the ship directory.");
            Assert.That(dock.GetOrphanedFiles().Any(o => o.Name == file), Is.False,
                "The restored file must no longer be an orphan.");

            // A file with an entry cannot be deleted as an orphan, and can once the entry is gone.
            Assert.That(dock.TryDeleteOrphanedFile(entry.File), Is.False,
                "A file with a dock entry must not be deletable through the orphan tool.");

            dock.RemoveEntry(entry, deleteFile: false);
            Assert.That(dock.TryDeleteOrphanedFile(entry.File), Is.True);
            Assert.That(res.UserData.Exists(new ResPath($"{BluespaceDockSystem.ShipDirectory}/{entry.File}")), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EntriesMatchByCkeyAndCharacterName()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var owned = new BluespaceDockEntry
            {
                OwnerUserId = "ckey-one",
                OwnerName = "Mike Winterbells",
            };

            // Normal entries need both the ckey and the character name.
            Assert.That(BluespaceDockSystem.EntryBelongsToCharacter(owned, "ckey-one", "Mike Winterbells"), Is.True);
            Assert.That(BluespaceDockSystem.EntryBelongsToCharacter(owned, "ckey-one", "Someone Else"), Is.False);
            Assert.That(BluespaceDockSystem.EntryBelongsToCharacter(owned, "ckey-two", "Mike Winterbells"), Is.False,
                "Another character of the same player must not see it.");

            // Admin-restored entries have no ckey (an ID card does not carry one) and match by name.
            var restored = new BluespaceDockEntry
            {
                OwnerUserId = string.Empty,
                OwnerName = "Mike Winterbells",
            };

            Assert.That(BluespaceDockSystem.EntryBelongsToCharacter(restored, "any-ckey", "Mike Winterbells"), Is.True);
            Assert.That(BluespaceDockSystem.EntryBelongsToCharacter(restored, "any-ckey", "Someone Else"), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CharacterNamePrefersTheProfile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            // The profile is what per-character data is keyed on (the bank does the same), so it wins;
            // the ID card is the fallback and the mob name the last resort. That is what lets a
            // character whose ID was lost or renamed still reach their own ships.
            Assert.That(BluespaceDockSystem.PickCharacterName("Valen", "Old Name", "Mob Name"), Is.EqualTo("Valen"));
            Assert.That(BluespaceDockSystem.PickCharacterName(null, "ID Name", "Mob Name"), Is.EqualTo("ID Name"));
            Assert.That(BluespaceDockSystem.PickCharacterName(null, null, "Mob Name"), Is.EqualTo("Mob Name"));
            Assert.That(BluespaceDockSystem.PickCharacterName("", "  ", null), Is.EqualTo(string.Empty));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipWarpPointsAreAddedOnceAndRenamed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            dock.EnsureShipWarpPoint(grid, "First Name");
            var warp = dock.FindShipWarpPoint(grid);
            Assert.That(warp, Is.Not.Null, "A ship with no warp point must get one.");
            Assert.That(warp!.Value.Comp.Location, Is.EqualTo("First Name"));
            Assert.That(warp.Value.Comp.UseStationName, Is.True);

            dock.EnsureShipWarpPoint(grid, "Second Name");
            Assert.That(dock.FindShipWarpPoint(grid)!.Value.Comp.Location, Is.EqualTo("Second Name"));

            var count = 0;
            var query = entMan.EntityQueryEnumerator<Content.Shared.Warps.WarpPointComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    count++;
            }

            Assert.That(count, Is.EqualTo(1), "Asking for a warp point twice must not add a second one.");

            entMan.DeleteEntity(warp.Value.Owner);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipFilesWithRemovedPrototypesStillLoad()
    {
        // An update can remove an entity prototype a stored ship uses. The engine's loader refuses the
        // whole file over one of those, so the dock hands them to the loader as deleted prototypes: the
        // entities (and anything stored inside them) are dropped instead, and the ship still loads.
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            // Something worth saving, so the file has an entity group to point at a removed prototype.
            entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new System.Numerics.Vector2(0.5f, 0.5f)));

            var file = BluespaceDockSystem.GetShipFilePath("legacy");
            Assert.That(dock.TrySaveShipFile(grid, file, out _), Is.True);

            // Pretend the update removed that prototype: rename it in the file to something gone.
            var path = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{file}");
            string text;
            using (var reader = res.UserData.OpenText(path))
            {
                text = reader.ReadToEnd();
            }

            Assert.That(text, Does.Contain("proto: ToolboxMechanicalFilled"), "sanity: the saved file names the prototype");
            var legacy = text.Replace("proto: ToolboxMechanicalFilled", "proto: PrototypeRemovedByAnUpdate");

            using (var writer = res.UserData.OpenWriteText(path))
            {
                writer.Write(legacy);
            }

            // The dock spots it...
            var missing = dock.CollectMissingPrototypes(path);
            Assert.That(missing, Does.Contain("PrototypeRemovedByAnUpdate"),
                "A prototype the ship uses that no longer exists must be detected.");

            // ...and still loads the ship.
            Assert.That(dock.TryLoadShipFile(file, out var loaded), Is.True,
                "A ship file whose prototypes were removed must still load.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            Assert.That(entMan.HasComponent<MapGridComponent>(loadedGrid), Is.True);

            // The file itself is left alone, so an admin can still recover it by hand.
            using (var reader = res.UserData.OpenText(path))
            {
                Assert.That(reader.ReadToEnd(), Is.EqualTo(legacy), "The original ship file must be left untouched.");
            }

            dock.DeleteShipFile(file);
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipFilesKeepContainerContents()
    {
        // A toolbox's contents used to be lost or duplicated on retrieval: the dock re-raised MapInit
        // on top of the engine's own initialization, so storage filled its prototype contents again.
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = server.System<ContainerSystem>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            var toolbox = entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new System.Numerics.Vector2(0.5f, 0.5f)));
            var duck = entMan.SpawnEntity("ToyRubberDuck", new EntityCoordinates(grid, new System.Numerics.Vector2(0.5f, 0.5f)));

            Assert.That(containers.TryGetContainer(toolbox, StorageComponent.ContainerId, out var storage), Is.True,
                "sanity: the toolbox has storage");
            Assert.That(containers.Insert(duck, storage!), Is.True, "sanity: the duck fits in the toolbox");

            var before = storage!.ContainedEntities.Count;
            Assert.That(before, Is.GreaterThan(0), "sanity: the toolbox has contents");

            var file = BluespaceDockSystem.GetShipFilePath("contents");
            Assert.That(dock.TrySaveShipFile(grid, file, out var error), Is.True, error);
            Assert.That(dock.TryLoadShipFile(file, out var loaded), Is.True);

            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;

            var found = false;
            var query = entMan.EntityQueryEnumerator<StorageComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != loadedGrid)
                    continue;

                found = true;
                Assert.That(containers.TryGetContainer(uid, StorageComponent.ContainerId, out var loadedStorage), Is.True);

                Assert.That(loadedStorage!.ContainedEntities.Count, Is.EqualTo(before),
                    "A container must come back with exactly what it was saved with - nothing lost, nothing duplicated.");

                var ducks = 0;
                foreach (var contained in loadedStorage.ContainedEntities)
                {
                    if (entMan.GetComponent<MetaDataComponent>(contained).EntityPrototype?.ID == "ToyRubberDuck")
                        ducks++;
                }

                Assert.That(ducks, Is.EqualTo(1), "The item put in the toolbox must still be in it.");
            }

            Assert.That(found, Is.True, "The loaded ship must still have its toolbox.");

            dock.DeleteShipFile(file);
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyCivilianGroupsAreAllowed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            Assert.That(BluespaceDockSystem.IsAllowedGroup(ShipyardConsoleUiKey.Shipyard.ToString()), Is.True, "Civilian ships are storable.");
            Assert.That(BluespaceDockSystem.IsAllowedGroup(ShipyardConsoleUiKey.Scrap.ToString()), Is.True, "Scrap ships are storable.");
            Assert.That(BluespaceDockSystem.IsAllowedGroup(ShipyardConsoleUiKey.Expedition.ToString()), Is.True, "Expedition ships are storable.");
            Assert.That(BluespaceDockSystem.IsAllowedGroup(ShipyardConsoleUiKey.Security.ToString()), Is.False, "NFSD ships are not storable.");
            Assert.That(BluespaceDockSystem.IsAllowedGroup(ShipyardConsoleUiKey.BlackMarket.ToString()), Is.False, "Black market ships are not storable.");
            Assert.That(BluespaceDockSystem.IsAllowedGroup(ShipyardConsoleUiKey.Sr.ToString()), Is.False, "SR ships are not storable.");
        });

        await pair.CleanReturnAsync();
    }
}
