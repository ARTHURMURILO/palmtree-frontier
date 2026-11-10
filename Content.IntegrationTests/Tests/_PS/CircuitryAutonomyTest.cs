#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryAutonomyTest
{
    [Test]
    public async Task CarriedItemsExposeSolutionsAndCanBeUsed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid cpu = default;
        EntityUid bucket = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var arm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);

            bucket = entMan.SpawnEntity("Bucket", map.GridCoords.Offset(new Vector2(1f, 0f)));
            entMan.SpawnEntity("Syringe", map.GridCoords.Offset(new Vector2(1f, 1f)));
            entMan.SpawnEntity("FlashlightLantern", map.GridCoords.Offset(new Vector2(0f, 1f)));
            entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2f, 0f)));

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
                    local bucket = robot.find("bucket")
                    local bucketStatus = robot.item_status(bucket)
                    local bucketSolution = bucketStatus.solutions[1]
                    log("bucket", bucketSolution.name, tostring(bucketSolution.volume))

                    local syringe = robot.find("syringe")
                    log("transfer", tostring(robot.transfer(bucket, syringe, 10)))
                    local syringeStatus = robot.item_status(syringe)
                    log("syringe", syringeStatus.injector.mode, tostring(robot.solution(syringe).volume))

                    local light = robot.find("flashlight")
                    log("light", tostring(robot.item_status(light).activated))

                    local person = robot.nearest_person(4)
                    if person then
                        log("pickup", tostring(robot.pickup(syringe)))
                        log("inject", tostring(robot.inject(person)))
                    else
                        log("person", "missing")
                    end
                end
                """;
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var manager = entMan.GetComponent<SolutionContainerManagerComponent>(bucket);
            Assert.That(solutions.TryGetSolution((bucket, manager), "drink", out var solution, out _), Is.True);
            Assert.That(solutions.TryAddReagent(solution!.Value, "Water", FixedPoint2.New(50), out _), Is.True);
        });

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("bucket\tdrink\t50"), log);
                Assert.That(log, Does.Contain("transfer\ttrue"), log);
                Assert.That(log, Does.Contain("syringe\tDraw\t10"), log);
                Assert.That(log, Does.Contain("light\tfalse"), log);
                Assert.That(log, Does.Contain("pickup\ttrue"), log);
                Assert.That(log, Does.Contain("inject\ttrue"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
