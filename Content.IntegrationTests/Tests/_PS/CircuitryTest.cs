#nullable enable
using System.Numerics;
using System.Linq;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Server.Hands.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Speech;
using NUnit.Framework;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryTest
{
    [Test]
    public async Task AssemblyLoadsModulesAndRunsLua()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid cpu = default;
        EntityUid cell = default;
        EntityUid wheels = default;
        EntityUid arm = default;
        EntityUid speaker = default;
        EntityUid sensor = default;
        EntityUid beaker = default;
        EntityUid beaker2 = default;
        EntityUid crowbar = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            wheels = entMan.SpawnEntity("CircuitWheels", map.GridCoords);
            arm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);
            speaker = entMan.SpawnEntity("CircuitSpeaker", map.GridCoords);
            sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            beaker = entMan.SpawnEntity("Beaker", map.GridCoords);
            beaker2 = entMan.SpawnEntity("Beaker", map.GridCoords);
            crowbar = entMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new Vector2(1f, 0f)));
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "locomotion", wheels, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "left_arm", arm, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "voice", speaker, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "beaker1", beaker, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "beaker2", beaker2, null), Is.True);
            });
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<CircuitAssemblyComponent>(assembly);
            var hands = entMan.System<HandsSystem>();
            var mover = entMan.GetComponent<InputMoverComponent>(assembly);
            var speech = entMan.GetComponent<SpeechComponent>(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(comp.Capabilities.HasFlag(CircuitCapability.Cpu), Is.True);
                Assert.That(comp.Capabilities.HasFlag(CircuitCapability.Power), Is.True);
                Assert.That(comp.Capabilities.HasFlag(CircuitCapability.Locomotion), Is.True);
                Assert.That(comp.Capabilities.HasFlag(CircuitCapability.Manipulator), Is.True);
                Assert.That(comp.Capabilities.HasFlag(CircuitCapability.Voice), Is.True);
                Assert.That(comp.Capabilities.HasFlag(CircuitCapability.Sensor), Is.True);
                Assert.That(comp.Capabilities.HasFlag(CircuitCapability.Reagent), Is.True);
                Assert.That(comp.Powered, Is.True);
                Assert.That(mover.CanMove, Is.True);
                Assert.That(speech.Enabled, Is.True);
                Assert.That(hands.TryGetHand(assembly, "hand_left_arm", out _), Is.True);
                Assert.That(entMan.System<UserInterfaceSystem>().HasUi(assembly, Content.Shared._PS.Circuitry.CircuitAssemblyUiKey.Key), Is.True);
                Assert.That(entMan.System<UserInterfaceSystem>().HasUi(cpu, Content.Shared._PS.Circuitry.CircuitCpuUiKey.Key), Is.True);
            });

            var cpuComp = entMan.GetComponent<CircuitCpuComponent>(cpu);
            cpuComp.Source = """
                function main()
                    log("start")
                    log("modules", tostring(robot.has_module("sensor")), tostring(robot.has_module("voice")), tostring(robot.has_module("manipulator")))
                    local self = robot.inspect(0)
                    log("self", self.name, self.kind, tostring(self.dead))
                    local things = robot.scan(4)
                    log("scan", tostring(#things))
                    local beakers = robot.beakers()
                    log("beakers", tostring(#beakers))
                    local reagents = robot.reagents(1)
                    log("reagents", tostring(reagents.volume))
                    local tool = robot.find("crowbar", 4)
                    if tool then
                        log("pickup", tostring(robot.pickup(tool)))
                        log("held", tostring(#robot.held()))
                        robot.drop()
                    end
                    local missing, missingReason = robot.find("definitely-not-here", 4)
                    log("missing", tostring(missing), tostring(missingReason))
                    robot.say("circuit test")
                    robot.move(1, 0)
                    robot.wait(0.1)
                    robot.stop()
                    log("done")
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        Vector2 startPosition = default;
        await server.WaitAssertion(() => startPosition = entMan.GetComponent<TransformComponent>(assembly).LocalPosition);
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
                Assert.That(log, Does.Contain("start"));
                Assert.That(log, Does.Contain("modules\ttrue\ttrue\ttrue"));
                Assert.That(log, Does.Contain("self\tprogrammable assembly\tmob\tfalse"));
                Assert.That(log, Does.Contain("scan\t"));
                Assert.That(log, Does.Contain("beakers\t2"));
                Assert.That(log, Does.Contain("reagents\t0"));
                Assert.That(log, Does.Contain("pickup\ttrue"));
                Assert.That(log, Does.Contain("held\t1"));
                Assert.That(log, Does.Contain("missing\tnil\tno matching entity in range"));
                Assert.That(log, Does.Contain("done"));
                Assert.That(entMan.GetComponent<TransformComponent>(assembly).LocalPosition.X, Is.GreaterThan(startPosition.X), "robot.move should move the assembly east");
            });
        });

        await server.WaitPost(() => Assert.That(entMan.System<ItemSlotsSystem>().TryEject(assembly, "cpu", null, out _), Is.True));
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() => Assert.That(brain.GetRuntime(assembly), Is.Null));
        await pair.CleanReturnAsync();
    }
}
