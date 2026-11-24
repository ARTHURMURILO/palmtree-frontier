using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Power.Components;
using Content.Server._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._PS.Construction;
using Content.Shared._PS.Construction.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.DoAfter;
using Content.Shared.Power;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Guards the turbine flatpack site works (reinforced plating and HV cabling) and the pack-up
///     cycle: a stopped turbine goes back into its flatpack, the deck is restored, and the turbine's
///     condition travels with the flatpack instead of being reset for free.
/// </summary>
[TestFixture]
public sealed class TurbineAssemblyTest
{
    [Test]
    public async Task FlatpackLaysSiteWorksAndPacksTurbineBackUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid flatpack = default;
        EntityUid turbine = default;
        List<Vector2i> footprintTiles = new();
        Vector2i center = default;

        await server.WaitAssertion(() =>
        {
            var plating = tiles["Plating"];
            for (var x = -4; x <= 4; x++)
            {
                for (var y = -4; y <= 4; y++)
                    mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            flatpack = entMan.SpawnEntity("GasTurbineFlatpack", testMap.GridCoords);
            turbine = entMan.SpawnEntity("Turbine", testMap.GridCoords);
            center = mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp, testMap.GridCoords);

            Assert.That(entMan.HasComponent<AnchorableComponent>(turbine), Is.False,
                "turbines are bolted down; packing up is the only way to move one");

            var assembly = entMan.GetComponent<TurbineAssemblyComponent>(turbine);
            Assert.That(assembly.Flatpack.Id, Is.EqualTo("GasTurbineFlatpack"));
            Assert.That(assembly.Deployed, Is.False, "a map-spawned turbine must not be packable");

            var comp = entMan.GetComponent<TurbineComponent>(turbine);
            comp.BladeHealth = 7;
            comp.Ruined = true;
            comp.StatorLoad = 42000f;
            comp.FlowRate = 123f;

            // The pack verb is the only in-game path, but the handler must also refuse an
            // undeployed turbine if the event is ever raised some other way.
            entMan.EventBus.RaiseLocalEvent(turbine, MakePackEvent(entMan, turbine));
            Assert.That(entMan.IsQueuedForDeletion(turbine), Is.False,
                "an undeployed turbine must not pack itself up");

            var footprint = entMan.GetComponent<FlatpackFootprintComponent>(flatpack);
            footprintTiles = Tiles(center, footprint).ToList();

            // The real unpack raises this on the spawned machine after the flatpack has spawned it.
            entMan.EventBus.RaiseLocalEvent(turbine,
                new FlatpackUnpackedEvent(flatpack, EntityUid.Invalid, turbine), true);

            Assert.That(assembly.Deployed, Is.True, "unpacking should make the turbine packable");
            Assert.That(assembly.Floors, Has.Count.EqualTo(footprintTiles.Count));
            Assert.That(assembly.CableTiles, Has.Count.EqualTo(footprintTiles.Count));

            var reinforced = tiles["FloorReinforced"];
            foreach (var pos in footprintTiles)
            {
                Assert.That(mapSys.TryGetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, pos, out var tileRef), Is.True);
                Assert.That(tileRef.Tile.TypeId, Is.EqualTo(reinforced.TileId),
                    $"tile {pos} should have been laid as reinforced plating");
                Assert.That(CountHvCables(entMan, mapSys, testMap.Grid.Owner, testMap.Grid.Comp, pos), Is.EqualTo(1),
                    $"tile {pos} should carry an HV cable");
            }
        });

        await server.WaitAssertion(() =>
        {
            // Do not count the original flatpack as the one spawned by packing up.
            entMan.DeleteEntity(flatpack);

            entMan.EventBus.RaiseLocalEvent(turbine, MakePackEvent(entMan, turbine));

            Assert.That(entMan.IsQueuedForDeletion(turbine), Is.True, "packing should remove the turbine");

            var plating = tiles["Plating"];
            foreach (var pos in footprintTiles)
            {
                Assert.That(mapSys.TryGetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, pos, out var tileRef), Is.True);
                Assert.That(tileRef.Tile.TypeId, Is.EqualTo(plating.TileId),
                    $"packing should restore the original deck at {pos}");
            }
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            EntityUid packed = default;
            var query = entMan.EntityQueryEnumerator<FlatpackComponent>();
            while (query.MoveNext(out var uid, out var pack))
            {
                if (pack.Entity?.Id != "Turbine")
                    continue;

                packed = uid;
                break;
            }

            Assert.That(packed, Is.Not.EqualTo(EntityUid.Invalid), "packing should spawn a turbine flatpack");
            Assert.That(entMan.GetComponent<FlatpackComponent>(packed).UnpackDelay, Is.EqualTo(TimeSpan.FromSeconds(4)));

            var state = entMan.GetComponent<TurbineFlatpackStateComponent>(packed);
            Assert.That(state.BladeHealth, Is.EqualTo(7));
            Assert.That(state.Ruined, Is.True);
            Assert.That(state.StatorLoad, Is.EqualTo(42000f));
            Assert.That(state.FlowRate, Is.EqualTo(123f));

            // Unpacking that flatpack must bring the damage and tuning back, not reset them.
            var unpacked = entMan.SpawnEntity("Turbine", testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(unpacked,
                new FlatpackUnpackedEvent(packed, EntityUid.Invalid, unpacked), true);

            var restored = entMan.GetComponent<TurbineComponent>(unpacked);
            Assert.That(restored.BladeHealth, Is.EqualTo(7));
            Assert.That(restored.Ruined, Is.True);
            Assert.That(restored.StatorLoad, Is.EqualTo(42000f));
            Assert.That(restored.FlowRate, Is.EqualTo(123f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SmallTurbineUsesItsOwnFlatpack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var small = entMan.SpawnEntity("TurbineSmall", testMap.GridCoords);
            var assembly = entMan.GetComponent<TurbineAssemblyComponent>(small);

            Assert.That(assembly.Flatpack.Id, Is.EqualTo("SmallGasTurbineFlatpack"));
            Assert.That(assembly.PackDelay, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(entMan.HasComponent<AnchorableComponent>(small), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FloorRestoreUsesPrototypeIdsAndSurvivesMapReload()
    {
        var savePath = new ResPath("/Maps/Test/TurbineFloorReload.yml");
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var resManager = server.ResolveDependency<IResourceManager>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            resManager.UserData.CreateDir(savePath.Directory);
            mapSys.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            LayFloor(mapSys, tiles, grid.Owner, grid.Comp);

            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var flatpack = entMan.SpawnEntity("GasTurbineFlatpack", coords);
            var turbine = entMan.SpawnEntity("Turbine", coords);
            var assembly = entMan.GetComponent<TurbineAssemblyComponent>(turbine);
            var footprint = entMan.GetComponent<FlatpackFootprintComponent>(flatpack);
            var center = mapSys.TileIndicesFor(grid.Owner, grid.Comp, coords);

            entMan.EventBus.RaiseLocalEvent(turbine, new FlatpackUnpackedEvent(flatpack, EntityUid.Invalid, turbine), true);

            var footprintTiles = Tiles(center, footprint).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(assembly.FloorTileId, Is.EqualTo("FloorReinforced"),
                    "the laid floor has to be persisted as a tile prototype id, not a session-local int");
                Assert.That(assembly.Floors, Has.Count.EqualTo(footprintTiles.Count));
                Assert.That(assembly.Floors.All(f => f.OriginalTileId == "Plating"),
                    "the original tile has to be persisted as a tile prototype id");
            });

            Assert.That(mapLoader.TrySaveMap(mapId, savePath), Is.True, "failed to save the test map");
            mapSys.DeleteMap(mapId);
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(savePath, out var loadedMap, out _), Is.True);
            var loadedMapId = loadedMap!.Value.Comp.MapId;

            EntityUid turbine = default;
            var turbineCount = 0;
            var turbineQuery = entMan.EntityQueryEnumerator<TurbineComponent>();
            while (turbineQuery.MoveNext(out var uid, out _))
            {
                turbine = uid;
                turbineCount++;
            }

            Assert.That(turbineCount, Is.EqualTo(1), "expected exactly one turbine on the loaded map");
            var assembly = entMan.GetComponent<TurbineAssemblyComponent>(turbine);
            var xform = entMan.GetComponent<TransformComponent>(turbine);
            Assert.That(xform.GridUid, Is.Not.Null, "the reloaded turbine should be anchored on the grid");

            Assert.Multiple(() =>
            {
                Assert.That(assembly.FloorTileId, Is.EqualTo("FloorReinforced"),
                    "the floor tile id did not survive the save/load");
                Assert.That(assembly.Floors.All(f => f.OriginalTileId == "Plating"),
                    "the original tile ids did not survive the save/load");
            });

            // The deck the flatpack laid is still there after the reload...
            var reinforced = tiles["FloorReinforced"];
            foreach (var floor in assembly.Floors)
            {
                Assert.That(mapSys.TryGetTileRef(xform.GridUid!.Value,
                        entMan.GetComponent<MapGridComponent>(xform.GridUid.Value), floor.Position, out var tileRef), Is.True);
                Assert.That(tileRef.Tile.TypeId, Is.EqualTo(reinforced.TileId));
            }

            // ...and packing up after a reload still restores the deck that was there before.
            // Packing clears the recorded floors, so keep a copy of the positions to check.
            var floors = assembly.Floors.Select(f => f.Position).ToList();
            var loadedGridUid = xform.GridUid!.Value;
            var loadedGrid = entMan.GetComponent<MapGridComponent>(loadedGridUid);

            entMan.EventBus.RaiseLocalEvent(turbine, MakePackEvent(entMan, turbine));
            Assert.That(entMan.IsQueuedForDeletion(turbine), Is.True, "packing should remove the turbine");

            var plating = tiles["Plating"];
            foreach (var pos in floors)
            {
                Assert.That(mapSys.TryGetTileRef(loadedGridUid, loadedGrid, pos, out var tileRef), Is.True);
                Assert.That(tileRef.Tile.TypeId, Is.EqualTo(plating.TileId),
                    $"packing after a reload did not restore the original deck at {pos}");
            }

            mapSys.DeleteMap(loadedMapId);
        });

        await server.WaitIdleAsync();
        await pair.CleanReturnAsync();
    }

    private static void LayFloor(SharedMapSystem mapSys, ITileDefinitionManager tiles, EntityUid gridUid, MapGridComponent grid)
    {
        var plating = tiles["Plating"];
        for (var x = -4; x <= 4; x++)
        {
            for (var y = -4; y <= 4; y++)
                mapSys.SetTile(gridUid, grid, new Vector2i(x, y), new Tile(plating.TileId));
        }
    }

    private static TurbinePackDoAfterEvent MakePackEvent(IEntityManager entMan, EntityUid turbine)
    {
        var ev = new TurbinePackDoAfterEvent();
        var args = new DoAfterArgs(entMan, turbine, TimeSpan.Zero, ev, turbine, target: turbine);
        ev.DoAfter = new Content.Shared.DoAfter.DoAfter(0, args, TimeSpan.Zero);
        return ev;
    }

    private static IEnumerable<Vector2i> Tiles(Vector2i center, FlatpackFootprintComponent footprint)
    {
        for (var x = 0; x < footprint.Footprint.X; x++)
        {
            for (var y = 0; y < footprint.Footprint.Y; y++)
                yield return center + footprint.Offset + new Vector2i(x, y);
        }
    }

    private static int CountHvCables(
        IEntityManager entMan,
        SharedMapSystem map,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i pos)
    {
        var count = 0;
        foreach (var anchored in map.GetAnchoredEntities((gridUid, grid), pos))
        {
            if (entMan.TryGetComponent<CableComponent>(anchored, out var cable) &&
                cable.CableType == CableType.HighVoltage)
            {
                count++;
            }
        }

        return count;
    }
}
