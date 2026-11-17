#nullable enable
using System.Collections.Generic;
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
public sealed class CircuitryChemistryTransferTest
{
    /// <summary>
    /// Records solution entities whose chemicals were marked changed. Source containers must be
    /// included; otherwise their sprite and examine window keep showing the old volume.
    /// </summary>
    public sealed class SolutionChangeProbeSystem : EntitySystem
    {
        public readonly List<EntityUid> Solutions = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<SolutionComponent, SolutionChangedEvent>(OnSolutionChanged);
        }

        private void OnSolutionChanged(EntityUid uid, SolutionComponent component, ref SolutionChangedEvent args)
        {
            Solutions.Add(uid);
        }
    }

    [Test]
    public async Task TransferUpdatesSourceSolutionAndReportsEmptySource()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid bucket = default;
        EntityUid beaker = default;
        EntityUid soil = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var arm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);
            beaker = entMan.SpawnEntity("Beaker", map.GridCoords);
            bucket = entMan.SpawnEntity("Bucket", map.GridCoords.Offset(new Vector2(1f, 0f)));
            soil = entMan.SpawnEntity("hydroponicsSoil", map.GridCoords.Offset(new Vector2(0f, 1f)));

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "left_arm", arm, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "beaker1", beaker, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local bucket = robot.find("bucket", 4)
                    local soil = robot.find("soil", 4)
                    if not bucket or not soil then
                        log("missing", tostring(bucket), tostring(soil))
                        return
                    end

                    local ok, reason = robot.transfer_to(soil, 20, 1)
                    log("water", tostring(ok), tostring(reason))

                    robot.wait(0.6)
                    ok, reason = robot.transfer_to(soil, 20, 1)
                    log("empty", tostring(ok), tostring(reason))

                    robot.wait(0.6)
                    ok, reason = robot.transfer_from(bucket, 20, 1)
                    log("refill", tostring(ok), tostring(reason))
                end
                """;
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var solutions = entMan.System<SharedSolutionContainerSystem>();

            var bucketManager = entMan.GetComponent<SolutionContainerManagerComponent>(bucket);
            Assert.That(solutions.TryGetSolution((bucket, bucketManager), "drink", out var bucketSolution, out _), Is.True);
            Assert.That(solutions.TryAddReagent(bucketSolution!.Value, "Water", FixedPoint2.New(100), out _), Is.True);

            var beakerManager = entMan.GetComponent<SolutionContainerManagerComponent>(beaker);
            Assert.That(solutions.TryGetSolution((beaker, beakerManager), "beaker", out var beakerSolution, out _), Is.True);
            Assert.That(solutions.TryAddReagent(beakerSolution!.Value, "Water", FixedPoint2.New(20), out _), Is.True);
        });

        var probe = server.ResolveDependency<IEntitySystemManager>()
            .GetEntitySystem<SolutionChangeProbeSystem>();

        await server.WaitPost(() => probe.Solutions.Clear());

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("water\ttrue\tnil"), log);
                Assert.That(log, Does.Contain("empty\tfalse\tbeaker slot 1 is empty"), log);
                Assert.That(log, Does.Contain("refill\ttrue\tnil"), log);
            });

            var solutions = entMan.System<SharedSolutionContainerSystem>();

            var bucketManager = entMan.GetComponent<SolutionContainerManagerComponent>(bucket);
            Assert.That(solutions.TryGetSolution((bucket, bucketManager), "drink", out var bucketSolution, out _), Is.True);
            Assert.That(bucketSolution!.Value.Comp.Solution.Volume, Is.EqualTo(FixedPoint2.New(80)));

            var beakerManager = entMan.GetComponent<SolutionContainerManagerComponent>(beaker);
            Assert.That(solutions.TryGetSolution((beaker, beakerManager), "beaker", out var beakerSolution, out _), Is.True);
            Assert.That(beakerSolution!.Value.Comp.Solution.Volume, Is.EqualTo(FixedPoint2.New(20)));

            var soilManager = entMan.GetComponent<SolutionContainerManagerComponent>(soil);
            Assert.That(solutions.TryGetSolution((soil, soilManager), "soil", out var soilSolution, out _), Is.True);
            Assert.That(soilSolution!.Value.Comp.Solution.Volume, Is.GreaterThan(FixedPoint2.Zero));

            // The beaker is changed twice: once as the source when watering and once as the
            // destination when refilled from the bucket. Without the source update the old code
            // only produced the refill event.
            var beakerChanges = probe.Solutions.FindAll(solution => solution == beakerSolution.Value.Owner);
            Assert.That(beakerChanges, Has.Count.GreaterThanOrEqualTo(2),
                "Draining the beaker must update the source solution so its UI is not stale.");

            Assert.That(probe.Solutions, Does.Contain(bucketSolution.Value.Owner),
                "Draining the bucket must update the source solution so its UI is not stale.");
        });

        await pair.CleanReturnAsync();
    }
}
