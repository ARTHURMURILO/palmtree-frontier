using System.Collections.Generic;
using System.Numerics;
using Content.Server._FarHorizons.Power.Generation.FissionGenerator;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared.Atmos;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Regression coverage for the gas turbine review: the destroyed flag has to survive a
///     save/load (and old saves that only stored blade health have to load as wrecked), and an
///     empty inlet must not turn the transfer calculation into NaN.
/// </summary>
[TestFixture]
public sealed class TurbineReviewFixesTest
{
    // The integration test harness has a 30 minute cap for the whole suite (PoolManagerTestEventHandler).
    // These tests must not create fresh (destructive) pairs: each one costs a full server+client boot
    // on CI. They use the pooled pair instead and clean up the maps they create.
    private static readonly ResPath RuinedSavePath = new("/Maps/Test/TurbineRuined.yml");
    private static readonly ResPath LegacySavePath = new("/Maps/Test/TurbineLegacy.yml");

    [Test]
    public async Task RuinedTurbineSurvivesSaveLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var resManager = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            resManager.UserData.CreateDir(RuinedSavePath.Directory);
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var turbine = entMan.SpawnEntity("Turbine", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));

            // Wrecked by overspeeding before the blades gave out: the health is still positive, so
            // only the persisted ruin flag can tell the reloaded turbine that it is destroyed.
            var comp = entMan.GetComponent<TurbineComponent>(turbine);
            comp.BladeHealth = 10;
            comp.Ruined = true;

            Assert.That(mapLoader.TrySaveMap(mapId, RuinedSavePath), Is.True);
            mapSystem.DeleteMap(mapId);
        });

        await server.WaitIdleAsync();

        EntityUid loaded = default;
        MapId loadedMapId = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(RuinedSavePath, out var loadedMap, out _), Is.True);
            loadedMapId = loadedMap!.Value.Comp.MapId;

            var turbines = new List<Entity<TurbineComponent>>();
            var query = entMan.EntityQueryEnumerator<TurbineComponent>();
            while (query.MoveNext(out var uid, out var comp))
                turbines.Add((uid, comp));

            Assert.That(turbines, Has.Count.EqualTo(1), "expected exactly one turbine after the load");
            loaded = turbines[0].Owner;
            var turbine = turbines[0].Comp;

            Assert.Multiple(() =>
            {
                Assert.That(turbine.Ruined, Is.True, "the ruin flag did not survive the save/load");
                Assert.That(turbine.BladeHealth, Is.EqualTo(10));
                Assert.That(turbine.IsSparking, Is.True, "damage indicators were not rebuilt on load");
                Assert.That(turbine.IsSmoking, Is.False, "damage indicators were not rebuilt on load");
            });

            // A turbine that came back intact would tear itself apart on the next atmos tick and
            // zero its RPM. Keep it spinning and make sure that does not happen.
            turbine.RPM = 500f;
            var ev = new AtmosDeviceUpdateEvent(1f, null, null);
            entMan.EventBus.RaiseLocalEvent(loaded, ref ev);

            Assert.That(entMan.GetComponent<TurbineComponent>(loaded).RPM, Is.EqualTo(500f),
                "a reloaded wreck ran TearApart again");

            mapSystem.DeleteMap(loadedMapId);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LegacyZeroHealthTurbineLoadsRuined()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var resManager = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            resManager.UserData.CreateDir(RuinedSavePath.Directory);
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var turbine = entMan.SpawnEntity("Turbine", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));

            // What a save written before the ruin flag was serialized looks like: zero health and
            // no ruined flag.
            var comp = entMan.GetComponent<TurbineComponent>(turbine);
            comp.BladeHealth = 0;
            comp.Ruined = false;

            Assert.That(mapLoader.TrySaveMap(mapId, LegacySavePath), Is.True);
            mapSystem.DeleteMap(mapId);
        });

        await server.WaitIdleAsync();

        MapId legacyLoadedMapId = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(LegacySavePath, out var legacyLoadedMap, out _), Is.True);
            legacyLoadedMapId = legacyLoadedMap!.Value.Comp.MapId;

            var turbines = new List<Entity<TurbineComponent>>();
            var query = entMan.EntityQueryEnumerator<TurbineComponent>();
            while (query.MoveNext(out var uid, out var comp))
                turbines.Add((uid, comp));

            Assert.That(turbines, Has.Count.EqualTo(1), "expected exactly one turbine after the load");
            var turbine = turbines[0].Comp;

            Assert.Multiple(() =>
            {
                Assert.That(turbine.Ruined, Is.True, "a zero-health turbine loaded intact");
                Assert.That(turbine.IsSparking, Is.True);
                Assert.That(turbine.IsSmoking, Is.True);
            });

            turbine.RPM = 500f;
            var ev = new AtmosDeviceUpdateEvent(1f, null, null);
            entMan.EventBus.RaiseLocalEvent(turbines[0].Owner, ref ev);

            Assert.That(turbine.RPM, Is.EqualTo(500f), "a zero-health turbine tore itself apart again on load");

            mapSystem.DeleteMap(legacyLoadedMapId);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmptyInletDoesNotProduceNaN()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var nodeContainer = entMan.System<NodeContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var turbine = entMan.SpawnEntity("Turbine", testMap.GridCoords);
            var comp = entMan.GetComponent<TurbineComponent>(turbine);

            // Attach the pipe stubs by hand. They are not part of a pipe net yet, so their air is
            // the immutable space mixture: zero moles at zero pressure, the 0/0 case.
            comp.InletEnt = entMan.SpawnAttachedTo("TurbineGasPipe",
                new EntityCoordinates(turbine, comp.InletPos), rotation: Angle.FromDegrees(comp.InletRot));
            comp.OutletEnt = entMan.SpawnAttachedTo("TurbineGasPipe",
                new EntityCoordinates(turbine, comp.OutletPos), rotation: Angle.FromDegrees(comp.OutletRot));

            Assert.That(nodeContainer.TryGetNode(comp.InletEnt.Value, comp.PipeName, out PipeNode? inlet), Is.True,
                "the turbine inlet has no pipe node");
            Assert.That(nodeContainer.TryGetNode(comp.OutletEnt.Value, comp.PipeName, out PipeNode? outlet), Is.True,
                "the turbine outlet has no pipe node");

            var ev = new AtmosDeviceUpdateEvent(1f, null, null);
            entMan.EventBus.RaiseLocalEvent(turbine, ref ev);

            Assert.Multiple(() =>
            {
                Assert.That(float.IsFinite(comp.RPM), Is.True, "empty inlet produced a non-finite RPM");
                Assert.That(float.IsFinite(comp.LastVolumeTransfer), Is.True, "empty inlet produced a non-finite transfer volume");
                Assert.That(float.IsFinite(comp.LastGen), Is.True, "empty inlet produced non-finite power");
                Assert.That(inlet!.Air.TotalMoles, Is.EqualTo(0f).Within(0.001f));
                Assert.That(float.IsFinite(inlet.Air.Pressure), Is.True);
                Assert.That(float.IsFinite(inlet.Air.Temperature), Is.True);
                Assert.That(float.IsFinite(outlet!.Air.Pressure), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }
}
