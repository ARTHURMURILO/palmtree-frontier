using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Piping.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
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
///     Guards the turbine pipe stubs across a map save/load. The stub entities are saved with the
///     map but the runtime <see cref="TurbineComponent.InletEnt"/>/<see cref="TurbineComponent.OutletEnt"/>
///     fields are not, so after a reload the turbine has to adopt the stubs that were already
///     saved instead of spawning a fresh pair on top of them.
/// </summary>
[TestFixture]
public sealed class TurbinePipeStubTest
{
    private static readonly ResPath SavePath = new("/Maps/Test/TurbinePipeStubs.yml");
    private const string StubPrototype = "TurbineGasPipe";

    [Test]
    public async Task PipeStubsAreReusedAfterSaveLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var resManager = server.ResolveDependency<IResourceManager>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        // Create a saved turbine with its two pipe stubs.
        await server.WaitAssertion(() =>
        {
            resManager.UserData.CreateDir(SavePath.Directory);
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            LayFloor(mapSystem, tiles, grid.Owner, grid.Comp);

            var turbine = entMan.SpawnEntity("Turbine",
                new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            var turbineComp = entMan.GetComponent<TurbineComponent>(turbine);

            Assert.That(entMan.GetComponent<TransformComponent>(turbine).Anchored, Is.True,
                "test setup: turbines spawn anchored");
            UpdateTurbine(entMan, turbine);
            Assert.Multiple(() =>
            {
                Assert.That(CountStubs(entMan), Is.EqualTo(2), "a fresh turbine should have one inlet and one outlet stub");
                Assert.That(StubUids(entMan).Contains(turbineComp.InletEnt!.Value), Is.True);
                Assert.That(StubUids(entMan).Contains(turbineComp.OutletEnt!.Value), Is.True);
            });

            Assert.That(mapLoader.TrySaveMap(mapId, SavePath), Is.True, "failed to save the test map");
            mapSystem.DeleteMap(mapId);
        });

        await server.WaitIdleAsync();

        // Reloading the map must not double the stubs: the first atmos tick adopts the saved ones.
        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(SavePath, out var loadedMap, out _), Is.True);
            var loadedMapId = loadedMap!.Value.Comp.MapId;

            var loadedTurbine = SingleTurbine(entMan);
            var turbineComp = entMan.GetComponent<TurbineComponent>(loadedTurbine);

            var savedStubs = StubUids(entMan);
            Assert.That(savedStubs, Has.Count.EqualTo(2), "the saved pipe stubs should load with the map");
            Assert.That(turbineComp.InletEnt, Is.Null, "the runtime stub fields must not be serialized");


            UpdateTurbine(entMan, loadedTurbine);
            Assert.Multiple(() =>
            {
                Assert.That(CountStubs(entMan), Is.EqualTo(2),
                    "the reloaded turbine spawned a second pair of pipe stubs instead of reusing the saved ones");
                Assert.That(savedStubs.Contains(turbineComp.InletEnt!.Value), Is.True,
                    "the inlet field should point at the stub that was saved with the map");
                Assert.That(savedStubs.Contains(turbineComp.OutletEnt!.Value), Is.True,
                    "the outlet field should point at the stub that was saved with the map");
                Assert.That(turbineComp.InletEnt, Is.Not.EqualTo(turbineComp.OutletEnt));
            });

            mapSystem.DeleteMap(loadedMapId);
        });

        await server.WaitIdleAsync();

        // Loading the same save a second time has to behave exactly the same.
        EntityUid secondTurbine = default;
        MapId secondMapId = default;
        List<EntityUid> secondLoadStubs = new();
        var xformSystem = entMan.System<SharedTransformSystem>();
        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(SavePath, out var secondLoad, out _), Is.True);
            Assert.That(secondLoad, Is.Not.Null);
            secondMapId = secondLoad!.Value.Comp.MapId;

            secondTurbine = SingleTurbine(entMan);
            secondLoadStubs = StubUids(entMan);
            Assert.That(secondLoadStubs, Has.Count.EqualTo(2));

            // A save written before the stubs were deduplicated by position could carry a duplicate
            // stub on the inlet tile. Adopting the stubs has to clean that duplicate up.
            var turbineComp = entMan.GetComponent<TurbineComponent>(secondTurbine);
            var inletMap = xformSystem.ToMapCoordinates(new EntityCoordinates(secondTurbine, turbineComp.InletPos));
            entMan.SpawnEntity(StubPrototype, inletMap);
            Assert.That(CountStubs(entMan), Is.EqualTo(3));

            UpdateTurbine(entMan, secondTurbine);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var turbineComp = entMan.GetComponent<TurbineComponent>(secondTurbine);
            Assert.Multiple(() =>
            {
                Assert.That(CountStubs(entMan), Is.EqualTo(2),
                    "the reloaded turbine spawned a duplicate pair or failed to clean up the extra stub");
                Assert.That(secondLoadStubs.Contains(turbineComp.InletEnt!.Value), Is.True,
                    "the inlet field should point at the stub that was saved with the map");
                Assert.That(secondLoadStubs.Contains(turbineComp.OutletEnt!.Value), Is.True);
            });

            mapSystem.DeleteMap(secondMapId);
        });

        await server.WaitIdleAsync();
        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Runs one turbine atmos tick, which is what lazily allocates/adopts the pipe stubs.
    /// </summary>
    private static void UpdateTurbine(IEntityManager entMan, EntityUid turbine)
    {
        var ev = new AtmosDeviceUpdateEvent(1f, null, null);
        entMan.EventBus.RaiseLocalEvent(turbine, ref ev);
    }

    private static EntityUid SingleTurbine(IEntityManager entMan)
    {
        EntityUid found = default;
        var count = 0;
        var query = entMan.EntityQueryEnumerator<TurbineComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            found = uid;
            count++;
        }

        Assert.That(count, Is.EqualTo(1), "expected exactly one turbine on the loaded map");
        return found;
    }

    private static List<EntityUid> StubUids(IEntityManager entMan)
    {
        var stubs = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == StubPrototype)
                stubs.Add(uid);
        }

        return stubs;
    }

    private static int CountStubs(IEntityManager entMan) => StubUids(entMan).Count;

    private static void LayFloor(SharedMapSystem map, ITileDefinitionManager tiles, EntityUid gridUid, MapGridComponent grid)
    {
        var plating = tiles["Plating"];
        for (var x = -3; x <= 3; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                map.SetTile(gridUid, grid, new Vector2i(x, y), new Tile(plating.TileId));
            }
        }
    }
}
