#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Server.Chat.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryEventTest
{
    [Test]
    public async Task EmotesDamageAndAttacksQueueEvents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid cpu = default;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            human = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    for i = 1, 4 do
                        local ev = robot.wait_event(3)
                        if ev then
                            log("ev", ev.type, ev.message, tostring(ev.damage_total), tostring(ev.emote))
                        else
                            log("ev", "timeout")
                        end
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
            entMan.System<ChatSystem>().TryEmoteWithoutChat(human, "Scream");

            var blunt = protoMan.EnumeratePrototypes<DamageTypePrototype>().First(proto => proto.ID == "Blunt");
            var damage = new DamageSpecifier(blunt, 5);
            entMan.System<DamageableSystem>().TryChangeDamage(assembly, damage, ignoreResistances: true);

            entMan.EventBus.RaiseLocalEvent(assembly,
                new AttackedEvent(assembly, human, map.GridCoords));

            var projectile = new ProjectileHitEvent(
                new DamageSpecifier(blunt, 7),
                assembly,
                human);
            entMan.EventBus.RaiseLocalEvent(assembly, ref projectile);
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("ev\temote"), log);
                Assert.That(log, Does.Contain("ev\tdamage"), log);
                Assert.That(log, Does.Contain("ev\tattacked"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
