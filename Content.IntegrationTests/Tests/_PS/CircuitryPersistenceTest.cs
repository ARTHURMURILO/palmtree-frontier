#nullable enable
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server._PS.Circuitry.Components;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryPersistenceTest
{
    [Test]
    public async Task SourceModulesLockAndOwnerSurviveGridSaveLoad()
    {
        var mapPath = new ResPath("/Maps/Test/CircuitrySaveLoad.yml");

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var resManager = server.ResolveDependency<IResourceManager>();

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;
        EntityUid assembly = default;

        await server.WaitAssertion(() =>
        {
            resManager.UserData.CreateDir(mapPath.Directory);
            mapSystem.CreateMap(out mapId);
            grid = mapManager.CreateGridEntity(mapId);

            // A grid with no tile at the spawn point gets detached by grid traversal. Real ships always
            // have floor tiles, so place one before spawning anything onto the grid.
            mapSystem.SetTile(grid, new Vector2i(0, 0), new Tile(typeId: 1, flags: 1, variant: 255));

            assembly = entMan.SpawnEntity("CircuitAssemblyFilled",
                new EntityCoordinates(grid.Owner, Vector2.Zero));
        });

        await pair.RunTicksSync(2);

        const string source = """
            function main()
                log("persisted")
            end
            """;

        await server.WaitAssertion(() =>
        {
            var component = entMan.GetComponent<CircuitAssemblyComponent>(assembly);
            var cpu = entMan.GetComponent<CircuitCpuComponent>(component.Cpu!.Value);
            cpu.Source = source;
            component.Locked = true;
            component.OwnerUserId = "test-user-id";
            component.OwnerName = "Persisted Owner";

            // This is the same save path used by Bluespace Dock (grid file, not whole-map file).
            Assert.That(mapLoader.TrySaveGrid(grid.Owner, mapPath, new SerializationOptions
            {
                MissingEntityBehaviour = MissingEntityBehaviour.IncludeNullspace,
                EntityExceptionBehaviour = EntityExceptionBehaviour.Rethrow,
                ErrorOnOrphan = false,
            }), Is.True);
        });

        await server.WaitIdleAsync();
        await server.WaitAssertion(() => mapSystem.DeleteMap(mapId));
        await server.WaitIdleAsync();

        EntityUid loadedGrid = default;
        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out mapId);
            Assert.That(mapLoader.TryLoadGrid(mapId, mapPath, out var gridUid), Is.True);
            loadedGrid = gridUid!.Value.Owner;
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<CircuitAssemblyComponent>();
            Assert.That(query.MoveNext(out var loadedAssembly, out var component), Is.True);
            Assert.That(entMan.GetComponent<TransformComponent>(loadedAssembly).GridUid, Is.EqualTo(loadedGrid));

            var cpu = component.Cpu;
            Assert.That(cpu, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<CircuitCpuComponent>(cpu!.Value).Source, Is.EqualTo(source));
                Assert.That(component.Locked, Is.True);
                Assert.That(component.OwnerUserId, Is.EqualTo("test-user-id"));
                Assert.That(component.OwnerName, Is.EqualTo("Persisted Owner"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AssemblySurvivesBluespaceDockRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var dock = server.System<BluespaceDockSystem>();
        var path = BluespaceDockSystem.GetShipFilePath("circuitry");

        const string source = """
            function main()
                log("docked")
            end
            """;

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var assembly = entMan.SpawnEntity("CircuitAssemblyFilled", testMap.GridCoords);
            var component = entMan.GetComponent<CircuitAssemblyComponent>(assembly);
            var cpu = entMan.GetComponent<CircuitCpuComponent>(component.Cpu!.Value);
            cpu.Source = source;
            component.Locked = true;
            component.OwnerUserId = "dock-user-id";
            component.OwnerName = "Docked Owner";

            Assert.That(dock.TrySaveShipFile(testMap.Grid.Owner, path, out var error), Is.True, error);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True);
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;

            var found = false;
            var query = entMan.EntityQueryEnumerator<CircuitAssemblyComponent>();
            while (query.MoveNext(out var uid, out var component))
            {
                if (entMan.GetComponent<TransformComponent>(uid).GridUid != loadedGrid)
                    continue;

                found = true;
                var cpu = component.Cpu;
                Assert.That(cpu, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(entMan.GetComponent<CircuitCpuComponent>(cpu!.Value).Source, Is.EqualTo(source));
                    Assert.That(component.Locked, Is.True);
                    Assert.That(component.OwnerUserId, Is.EqualTo("dock-user-id"));
                    Assert.That(component.OwnerName, Is.EqualTo("Docked Owner"));
                    Assert.That(component.Hands, Is.EqualTo(2));
                });
            }

            Assert.That(found, Is.True, "The docked ship should still contain the circuit assembly.");
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => mapSystem.DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }
}
