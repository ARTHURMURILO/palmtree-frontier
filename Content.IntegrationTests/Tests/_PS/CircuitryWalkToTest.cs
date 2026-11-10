#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryWalkToTest
{
    [Test]
    public async Task BlockingWalkToWaitsForArrival()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var tileMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;

        await server.WaitPost(() =>
        {
            var plating = new Tile(tileMan["Plating"].TileId);
            for (var x = 0; x <= 5; x++)
                mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), plating);

            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var wheels = entMan.SpawnEntity("CircuitWheels", map.GridCoords);

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "locomotion", wheels, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local ok, reason = robot.walk_to(3.5, 0.5)
                    local x = robot.position()
                    log("walk", tostring(ok), tostring(reason), tostring(x))
                end
                """;
        });

        // Tile changes dirty the navmesh; let the pathfinding system rebuild its chunk.
        await pair.RunTicksSync(60);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.StartWith("walk\ttrue"), log);
            Assert.That(entMan.GetComponent<TransformComponent>(assembly).LocalPosition.X, Is.GreaterThan(2f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AsyncWalkToReturnsBeforeArrival()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var tileMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;

        await server.WaitPost(() =>
        {
            var plating = new Tile(tileMan["Plating"].TileId);
            for (var x = 0; x <= 5; x++)
                mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), plating);

            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var wheels = entMan.SpawnEntity("CircuitWheels", map.GridCoords);

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "locomotion", wheels, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local ok = robot.walk_to_async(3.5, 0.5)
                    local x = robot.position()
                    log("async", tostring(ok), robot.walk_status(), tostring(x))
                end
                """;
        });

        // Tile changes dirty the navmesh; let the pathfinding system rebuild its chunk.
        await pair.RunTicksSync(60);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.StartWith("async\ttrue\tmoving\t"), log);
            Assert.That(entMan.GetComponent<TransformComponent>(assembly).LocalPosition.X, Is.LessThan(2f));
        });

        await pair.CleanReturnAsync();
    }
}
