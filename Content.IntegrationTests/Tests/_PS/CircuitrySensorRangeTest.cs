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
public sealed class CircuitrySensorRangeTest
{
    [Test]
    public async Task FindUsesTheConfiguredTileRange()
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
            for (var x = 0; x <= 17; x++)
                mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), plating);

            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);

            var wrench = entMan.SpawnEntity("Wrench",
                new EntityCoordinates(map.Grid.Owner, new Vector2(15.5f, 0.5f)));
            entMan.System<MetaDataSystem>().SetEntityName(wrench, "target wrench");

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    log("find", tostring(robot.find("target wrench")))
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitAssertion(() =>
        {
            var component = entMan.GetComponent<CircuitAssemblyComponent>(assembly);
            Assert.That(component.SensorRange, Is.EqualTo(16f));
        });

        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Not.Contain("find\tnil"), log);
            Assert.That(log, Does.StartWith("find\t"), log);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FindSeesAMobAtFiveTiles()
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
            for (var x = 0; x <= 6; x++)
                mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), plating);

            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);

            var target = entMan.SpawnEntity("MobHuman",
                new EntityCoordinates(map.Grid.Owner, new Vector2(5.5f, 0.5f)));
            entMan.System<MetaDataSystem>().SetEntityName(target, "target person");

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local target = robot.find("target person")
                    log("find", tostring(target))
                    if target then
                        log("distance", tostring(robot.distance(target)))
                        log("inspect", tostring(robot.inspect(target) ~= nil))
                    end
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Not.Contain("find\tnil"), log);
            Assert.That(log, Does.Contain("distance\t5"), log);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FindIgnoresTheMultiScanResultCap()
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
            for (var x = 0; x <= 6; x++)
                mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), plating);

            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);

            // More decoys than maxScanResults (16), all closer than the target. This is the
            // dense-room case where find() used to stop seeing anything past the 16 nearest.
            for (var i = 0; i < 24; i++)
            {
                var decoy = entMan.SpawnEntity("Wrench",
                    new EntityCoordinates(map.Grid.Owner, new Vector2(1f + i * 0.05f, 0.5f)));
                entMan.System<MetaDataSystem>().SetEntityName(decoy, "closer wrench");
            }

            var target = entMan.SpawnEntity("Wrench",
                new EntityCoordinates(map.Grid.Owner, new Vector2(5.5f, 0.5f)));
            entMan.System<MetaDataSystem>().SetEntityName(target, "far target wrench");

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    log("find", tostring(robot.find("far target wrench")))
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Not.Contain("find\tnil"), log);
            Assert.That(log, Does.StartWith("find\t"), log);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TightFindLoopIsRateLimited()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var wrench = entMan.SpawnEntity("Wrench", map.GridCoords.Offset(new Vector2(1f, 0f)));
            entMan.System<MetaDataSystem>().SetEntityName(wrench, "target wrench");

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local found = 0
                    local limited = 0
                    for i = 1, 300 do
                        local target, reason = robot.find("target wrench")
                        if target then
                            found = found + 1
                        elseif reason == "scan cooldown" then
                            limited = limited + 1
                        end
                    end
                    log("found", found)
                    log("limited", limited)
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Contain("limited\t"), log);
            Assert.That(log, Does.Not.Contain("limited\t0"), log);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WalkToAsyncStartsAreRateLimited()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;

        await server.WaitPost(() =>
        {
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
                    local first = robot.walk_to_async(10, 0)
                    local second, reason = robot.walk_to_async(10, 0)
                    log("first", tostring(first))
                    log("second", tostring(second), tostring(reason))
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Contain("first\ttrue"), log);
            Assert.That(log, Does.Contain("second\tfalse\twalk cooldown"), log);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UserFollowLoopDoesNotTripTheAllocationWatchdog()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var wheels = entMan.SpawnEntity("CircuitWheels", map.GridCoords);
            var target = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(5f, 0f)));
            entMan.System<MetaDataSystem>().SetEntityName(target, "octavia");

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "locomotion", wheels, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    while true do
                        local me = robot.find("octavia")
                        local x,y = robot.position_of(me)
                        robot.walk_to(x,y)
                    end
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(runtime.Program.LastError ?? string.Empty, Does.Not.Contain("allocated"), log);
        });

        await pair.CleanReturnAsync();
    }
}
