#nullable enable
using System;
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitrySyncActionTest
{
    [Test]
    public async Task SyncInjectWaitsForTheDoAfter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid syringe = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var arm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);

            syringe = entMan.SpawnEntity("Syringe", map.GridCoords.Offset(new Vector2(1f, 0f)));
            entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));

            var injector = entMan.GetComponent<InjectorComponent>(syringe);
            injector.ToggleState = InjectorToggleMode.Inject;
            injector.Delay = TimeSpan.FromSeconds(0.5);
            injector.DelayPerVolume = TimeSpan.Zero;

            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var manager = entMan.GetComponent<SolutionContainerManagerComponent>(syringe);
            Assert.That(solutions.TryGetSolution((syringe, manager), injector.SolutionName, out var solution, out _),
                Is.True);
            Assert.That(solutions.TryAddReagent(solution!.Value, "Water", FixedPoint2.New(10), out _), Is.True);

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
                    local person = robot.nearest_person(4)
                    local injector = robot.find("syringe")
                    log("pickup", tostring(robot.pickup(injector)))
                    log("before")
                    log("inject", tostring(robot.inject(person)))
                    log("after")
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Contain("before"), log);
            Assert.That(log, Does.Not.Contain("after"), log);
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Contain("inject\ttrue"), log);
            Assert.That(log, Does.Contain("after"), log);
        });

        await pair.CleanReturnAsync();
    }
}
