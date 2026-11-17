#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Server.Hands.Systems;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryShootTest
{
    [Test]
    public async Task ShootUsesSightRangeRatherThanHandRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid gun = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var arm = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);
            gun = entMan.SpawnEntity("NFWeaponEnergySubMachineGunDeltaV", map.GridCoords);

            var target = entMan.SpawnEntity("MobHuman",
                new EntityCoordinates(map.Grid.Owner, new Vector2(5f, 0f)));
            entMan.System<MetaDataSystem>().SetEntityName(target, "shoot target");

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
                    local target = robot.find("shoot target")
                    local ok, reason = robot.shoot(target)
                    log("shoot", tostring(ok), tostring(reason))
                end
                """;
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            Assert.That(entMan.System<HandsSystem>().TryPickupAnyHand(assembly, gun), Is.True);
        });

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Contain("shoot\ttrue"), log);
            Assert.That(log, Does.Not.Contain("out of reach"), log);
            Assert.That(log, Does.Not.Contain("out of sight"), log);
        });

        await pair.CleanReturnAsync();
    }
}
