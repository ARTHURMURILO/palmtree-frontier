#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitrySignalTest
{
    [Test]
    public async Task SignalsCanBeSentAndReceivedThroughRadioModule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid receiver = default;
        EntityUid sender = default;

        await server.WaitPost(() =>
        {
            receiver = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            sender = entMan.SpawnEntity("CircuitAssembly", map.GridCoords.Offset(new Vector2(1f, 0f)));

            entMan.System<MetaDataSystem>().SetEntityName(receiver, "Receiver");
            entMan.System<MetaDataSystem>().SetEntityName(sender, "Sender");

            var slots = entMan.System<ItemSlotsSystem>();
            var receiverCpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var receiverCell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var receiverRadio = entMan.SpawnEntity("CircuitRadio", map.GridCoords);

            var senderCpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var senderCell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var senderRadio = entMan.SpawnEntity("CircuitRadio", map.GridCoords);
            var senderSensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var senderArm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(receiver, "cpu", receiverCpu, null), Is.True);
                Assert.That(slots.TryInsert(receiver, "cell", receiverCell, null), Is.True);
                Assert.That(slots.TryInsert(receiver, "radio", receiverRadio, null), Is.True);
                Assert.That(slots.TryInsert(sender, "cpu", senderCpu, null), Is.True);
                Assert.That(slots.TryInsert(sender, "cell", senderCell, null), Is.True);
                Assert.That(slots.TryInsert(sender, "radio", senderRadio, null), Is.True);
                Assert.That(slots.TryInsert(sender, "sensor", senderSensor, null), Is.True);
                Assert.That(slots.TryInsert(sender, "left_arm", senderArm, null), Is.True);
            });

            var signaller = entMan.SpawnEntity("RemoteSignaller", map.GridCoords.Offset(new Vector2(1.5f, 0f)));
            var deviceLink = entMan.System<DeviceLinkSystem>();
            deviceLink.SaveLinks(null, sender, receiver, new List<(string, string)>
            {
                ("Pressed", "Trigger"),
                ("On", "On"),
            });
            deviceLink.SaveLinks(null, signaller, receiver, new List<(string, string)>
            {
                ("Pressed", "Trigger"),
            });

            entMan.GetComponent<CircuitCpuComponent>(receiverCpu).Source = """
                function main()
                    for i = 1, 3 do
                        local ev = robot.wait_event(2)
                        if ev then
                            log("signal", ev.type, ev.port, tostring(ev.state), ev.name)
                        else
                            log("signal", "none")
                        end
                    end
                end
                """;

            entMan.GetComponent<CircuitCpuComponent>(senderCpu).Source = """
                function main()
                    robot.wait(0.3)
                    log("send", tostring(robot.send_signal(true)))

                    robot.wait(0.6)
                    log("sendOn", tostring(robot.send_signal(true, "On")))

                    robot.wait(0.6)
                    local signaller = robot.find("remote signaller")
                    if signaller then
                        log("pickup", tostring(robot.pickup(signaller)))
                        log("trigger", tostring(robot.trigger(signaller)))
                    else
                        log("signaller", "missing")
                    end
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(receiver), Is.True));
        await pair.RunTicksSync(2);
        await server.WaitPost(() => Assert.That(brain.Start(sender), Is.True));
        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var receiverRuntime = brain.GetRuntime(receiver);
            var senderRuntime = brain.GetRuntime(sender);

            Assert.Multiple(() =>
            {
                Assert.That(receiverRuntime, Is.Not.Null);
                Assert.That(senderRuntime, Is.Not.Null);

                var receiverLog = string.Join("\n", receiverRuntime!.Program.Log);
                var senderLog = string.Join("\n", senderRuntime!.Program.Log);

                Assert.That(receiverRuntime.Program.LastError, Is.Null, receiverLog);
                Assert.That(senderRuntime.Program.LastError, Is.Null, senderLog);
                Assert.That(senderLog, Does.Contain("send\ttrue"), senderLog);
                Assert.That(senderLog, Does.Contain("sendOn\ttrue"), senderLog);
                Assert.That(senderLog, Does.Contain("pickup\ttrue"), senderLog);
                Assert.That(senderLog, Does.Contain("trigger\ttrue"), senderLog);
                Assert.That(receiverLog, Does.Contain("signal\tsignal\tTrigger\ttrue\tSender"), receiverLog);
                Assert.That(receiverLog, Does.Contain("signal\tsignal\tOn\ttrue\tSender"), receiverLog);
                Assert.That(receiverLog, Does.Contain("signal\tsignal\tTrigger\ttrue\tremote signaller"), receiverLog);
            });
        });

        await pair.CleanReturnAsync();
    }
}
