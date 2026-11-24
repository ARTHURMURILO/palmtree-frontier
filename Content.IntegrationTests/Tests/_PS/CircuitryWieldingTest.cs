#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryWieldingTest
{
    [Test]
    public async Task TriggerWieldsTwoHandedGun()
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
            var leftArm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);
            var rightArm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);

            var gun = entMan.SpawnEntity("NFWeaponEnergySubMachineGunDeltaV",
                map.GridCoords.Offset(new Vector2(1f, 0f)));
            entMan.AddComponent<GunRequiresWieldComponent>(gun);
            entMan.System<MetaDataSystem>().SetEntityName(gun, "wield test gun");

            var target = entMan.SpawnEntity("MobHuman",
                map.GridCoords.Offset(new Vector2(5f, 0f)));
            entMan.System<MetaDataSystem>().SetEntityName(target, "wield target");

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "left_arm", leftArm, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "right_arm", rightArm, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local gun = robot.find("wield test gun", 4)
                    local target = robot.find("wield target", 6)
                    if not gun or not target then
                        log("missing", tostring(gun), tostring(target))
                        return
                    end

                    log("pickup", tostring(robot.pickup(gun)))

                    local ok, reason = robot.shoot(target)
                    log("unwielded_shoot", tostring(ok), tostring(reason))

                    ok, reason = robot.wield(true)
                    log("wield", tostring(ok), tostring(reason))
                    log("wielded", tostring(robot.item_status(gun).wielded))

                    robot.wait(1.1)
                    ok, reason = robot.shoot(target)
                    log("wielded_shoot", tostring(ok), tostring(reason))

                    robot.wait(1.1)
                    ok, reason = robot.wield(false)
                    log("unwield", tostring(ok), tostring(reason))
                    log("wielded_after", tostring(robot.item_status(gun).wielded))
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
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("pickup\ttrue"), log);
                Assert.That(log, Does.Contain("unwielded_shoot\tfalse\tweapon must be wielded"), log);
                Assert.That(log, Does.Contain("wield\ttrue"), log);
                Assert.That(log, Does.Contain("wielded\ttrue"), log);
                Assert.That(log, Does.Contain("wielded_shoot\ttrue"), log);
                Assert.That(log, Does.Contain("unwield\ttrue"), log);
                Assert.That(log, Does.Contain("wielded_after\tfalse"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
