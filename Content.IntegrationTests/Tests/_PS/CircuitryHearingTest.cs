#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Server.Chat.Systems;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryHearingTest
{
    [Test]
    public async Task SpeechIsQueuedForLua()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid cpu = default;
        EntityUid cell = default;
        EntityUid sensor = default;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            human = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));

            var slots = entMan.System<ItemSlotsSystem>();
            slots.TryInsert(assembly, "cpu", cpu, null);
            slots.TryInsert(assembly, "cell", cell, null);
            slots.TryInsert(assembly, "sensor", sensor, null);

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local ev = robot.wait_event(3)
                    if ev then
                        log("heard", ev.type, ev.name, ev.message)
                    else
                        log("timeout")
                    end
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var chat = entMan.System<ChatSystem>();
            chat.TrySendInGameICMessage(
                human,
                "help me",
                InGameICChatType.Speak,
                hideChat: false,
                hideLog: false,
                player: null);
        });

        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("heard\tspeech"));
                Assert.That(log, Does.Contain("Help me"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
