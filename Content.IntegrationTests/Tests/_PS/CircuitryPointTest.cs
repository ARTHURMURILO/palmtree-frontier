#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Server.Pointing.Components;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryPointTest
{
    [Test]
    public async Task PointSpawnsAPointingArrow()
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
            var arm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);
            var target = entMan.SpawnEntity("MobHuman",
                new EntityCoordinates(map.Grid.Owner, new Vector2(5f, 0f)));
            entMan.System<MetaDataSystem>().SetEntityName(target, "point target");

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "left_arm", arm, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local target = robot.find("point target")
                    local ok, reason = robot.point(target)
                    log("point", tostring(ok), tostring(reason))
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
            Assert.That(log, Does.Contain("point\ttrue"), log);

            var arrows = 0;
            var query = entMan.EntityQueryEnumerator<PointingArrowComponent>();
            while (query.MoveNext(out _, out _))
                arrows++;

            Assert.That(arrows, Is.GreaterThan(0), "robot.point should spawn a PointingArrow.");
        });

        await pair.CleanReturnAsync();
    }
}
