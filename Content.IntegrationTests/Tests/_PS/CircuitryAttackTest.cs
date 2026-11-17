#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryAttackTest
{
    [Test]
    public async Task AttackEnablesCombatModeAndSwings()
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

            entMan.SpawnEntity("FireAxe", map.GridCoords);
            var target = entMan.SpawnEntity("MobHuman",
                new EntityCoordinates(map.Grid.Owner, new Vector2(1f, 0f)));
            entMan.System<MetaDataSystem>().SetEntityName(target, "attack target");

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
                    local axe = robot.find("fireaxe")
                    log("pickup", tostring(robot.pickup(axe)))
                    robot.wait(1.5)

                    local target = robot.find("attack target")
                    local ok, reason = robot.attack(target)
                    log("attack", tostring(ok), tostring(reason))
                    log("combat", tostring(robot.combat()))
                    log("combat_on", tostring(robot.combat(true)))
                    log("combat_off", tostring(robot.combat(false)))
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
            Assert.That(log, Does.Contain("attack\ttrue"), log);
            Assert.That(log, Does.Contain("combat\tfalse"), log);
            Assert.That(log, Does.Contain("combat_on\ttrue"), log);
            Assert.That(log, Does.Contain("combat_off\tfalse"), log);
            Assert.That(entMan.GetComponent<CombatModeComponent>(assembly).IsInCombatMode, Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
