#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryRadioTest
{
    [Test]
    public async Task RadioUsesEncryptionKeysAndQueuesMessages()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid listener = default;
        EntityUid sender = default;

        await server.WaitPost(() =>
        {
            listener = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            sender = entMan.SpawnEntity("CircuitAssembly", map.GridCoords.Offset(new Vector2(1f, 0f)));

            var slots = entMan.System<ItemSlotsSystem>();
            var listenerCpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var listenerCell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var listenerRadio = entMan.SpawnEntity("CircuitRadio", map.GridCoords);
            var senderCpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var senderCell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var senderRadio = entMan.SpawnEntity("CircuitRadio", map.GridCoords);
            var listenerKey = entMan.SpawnEntity("EncryptionKeySecurity", map.GridCoords);
            var senderKey = entMan.SpawnEntity("EncryptionKeySecurity", map.GridCoords);
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(listener, "cpu", listenerCpu, null), Is.True);
                Assert.That(slots.TryInsert(listener, "cell", listenerCell, null), Is.True);
                Assert.That(slots.TryInsert(listener, "radio", listenerRadio, null), Is.True);
                Assert.That(slots.TryInsert(sender, "cpu", senderCpu, null), Is.True);
                Assert.That(slots.TryInsert(sender, "cell", senderCell, null), Is.True);
                Assert.That(slots.TryInsert(sender, "radio", senderRadio, null), Is.True);
            });

            // Encryption keys are used on the chassis and forwarded into the radio module.
            var listenerKeyEvent = new InteractUsingEvent(user, listenerKey, listener, map.GridCoords);
            var senderKeyEvent = new InteractUsingEvent(user, senderKey, sender, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(listener, listenerKeyEvent);
            entMan.EventBus.RaiseLocalEvent(sender, senderKeyEvent);
            Assert.That(listenerKeyEvent.Handled, Is.True);
            Assert.That(senderKeyEvent.Handled, Is.True);

            entMan.GetComponent<CircuitCpuComponent>(listenerCpu).Source = """
                function main()
                    local ev = robot.wait_event(2)
                    if ev then
                        log("event", ev.type, ev.channel, ev.message)
                    else
                        log("event", "none")
                    end
                end
                """;

            entMan.GetComponent<CircuitCpuComponent>(senderCpu).Source = """
                function main()
                    robot.wait(0.3)
                    local channels = robot.radio_channels()
                    log("channels", tostring(#channels), channels[1].id, channels[2].id)
                    local ok, err = robot.radio("security sweep complete", "Security")
                    log("sent", tostring(ok), err or "")
                    local denied, deniedErr = robot.radio("should not work", "Command")
                    log("denied", tostring(denied), deniedErr or "")
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(listener), Is.True));
        await pair.RunTicksSync(2);
        await server.WaitPost(() => Assert.That(brain.Start(sender), Is.True));
        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var listenerRuntime = brain.GetRuntime(listener);
            var senderRuntime = brain.GetRuntime(sender);

            Assert.Multiple(() =>
            {
                Assert.That(listenerRuntime, Is.Not.Null);
                Assert.That(senderRuntime, Is.Not.Null);

                var listenerLog = string.Join("\n", listenerRuntime!.Program.Log);
                var senderLog = string.Join("\n", senderRuntime!.Program.Log);

                Assert.That(listenerRuntime.Program.LastError, Is.Null, listenerLog);
                Assert.That(senderRuntime.Program.LastError, Is.Null, senderLog);
                Assert.That(senderLog, Does.Contain("channels\t2\tCommon\tSecurity"), senderLog);
                Assert.That(senderLog, Does.Contain("sent\ttrue"), senderLog);
                Assert.That(senderLog, Does.Contain("denied\tfalse"), senderLog);
                Assert.That(listenerLog, Does.Contain("event\tradio\tSecurity\tsecurity sweep complete"), listenerLog);
            });
        });

        await pair.CleanReturnAsync();
    }
}
