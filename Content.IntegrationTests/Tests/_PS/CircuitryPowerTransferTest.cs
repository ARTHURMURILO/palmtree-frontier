#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryPowerTransferTest
{
    [Test]
    public async Task DrawsFromNearbyPowerAndChargesBatteries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid assemblyCell = default;
        EntityUid targetCell = default;
        EntityUid apc = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            var cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            assemblyCell = entMan.SpawnEntity("PowerCellSmallPrinted", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);

            var battery = entMan.GetComponent<BatteryComponent>(assemblyCell);
            entMan.System<BatterySystem>().SetCharge(assemblyCell, 100f, battery);

            targetCell = entMan.SpawnEntity("PowerCellSmallPrinted",
                new EntityCoordinates(map.Grid.Owner, new Vector2(1f, 0f)));
            entMan.System<MetaDataSystem>().SetEntityName(targetCell, "test cell");

            apc = entMan.SpawnEntity("APCBasic",
                new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0f)));

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", assemblyCell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local from = robot.charge_from(50)
                    local selfBattery = robot.battery(0)
                    log("from", tostring(from), tostring(selfBattery.charge), tostring(selfBattery.max))

                    robot.wait(0.5)

                    local target = robot.find("test cell")
                    local targetBefore = robot.battery(target)
                    local charged = robot.charge(target, 40)
                    local targetAfter = robot.battery(target)
                    log("battery", tostring(targetBefore.charge), tostring(targetBefore.max))
                    log("charge", tostring(charged), tostring(targetAfter.charge))

                    local finalSelf = robot.battery(0)
                    log("self", tostring(finalSelf.charge), tostring(finalSelf.max))
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.That(runtime.Program.LastError, Is.Null, log);
            Assert.That(log, Does.Contain("from\ttrue"), log);
            Assert.That(log, Does.Contain("battery\t0\t360"), log);
            Assert.That(log, Does.Contain("charge\ttrue\t40"), log);

            var selfCharge = entMan.GetComponent<BatteryComponent>(assemblyCell).CurrentCharge;
            var targetCharge = entMan.GetComponent<BatteryComponent>(targetCell).CurrentCharge;
            var apcCharge = entMan.GetComponent<BatteryComponent>(apc).CurrentCharge;

            Assert.Multiple(() =>
            {
                Assert.That(selfCharge, Is.EqualTo(110f).Within(5f));
                Assert.That(targetCharge, Is.EqualTo(40f).Within(1f));
                Assert.That(apcCharge, Is.LessThan(50000f));
            });
        });

        await pair.CleanReturnAsync();
    }
}
