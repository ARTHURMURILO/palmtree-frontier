#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryChemMasterTest
{
    [Test]
    public async Task ChemMasterTransferAndPillProductionWork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid cpu = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            var manipulator = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);

            var chemMaster = entMan.SpawnEntity("ChemMaster", map.GridCoords.Offset(new Vector2(1f, 0f)));
            var beaker = entMan.SpawnEntity("CryoxadoneBeakerSmall", map.GridCoords);
            var pillCanister = entMan.SpawnEntity("PillCanister", map.GridCoords);

            var bottleMaster = entMan.SpawnEntity("ChemMaster", map.GridCoords.Offset(new Vector2(0f, 1f)));
            entMan.System<MetaDataSystem>().SetEntityName(bottleMaster, "Bottle Master");
            var bottleBeaker = entMan.SpawnEntity("CryoxadoneBeakerSmall", map.GridCoords);
            var bottle = entMan.SpawnEntity("ChemistryEmptyBottle01", map.GridCoords);

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "left_arm", manipulator, null), Is.True);
                Assert.That(slots.TryInsert(chemMaster, "beakerSlot", beaker, null), Is.True);
                Assert.That(slots.TryInsert(chemMaster, "outputSlot", pillCanister, null), Is.True);
                Assert.That(slots.TryInsert(bottleMaster, "beakerSlot", bottleBeaker, null), Is.True);
                Assert.That(slots.TryInsert(bottleMaster, "outputSlot", bottle, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local master = robot.find("chem", 4)
                    if not master then
                        log("chem_master", "missing")
                        return
                    end

                    local status = robot.chem_master_status(master)
                    log("cm_status", status.mode, tostring(status.pill_dosage_limit),
                        tostring(status.buffer.volume))

                    log("cm_transfer",
                        tostring(robot.chem_master_transfer(master, "Cryoxadone", 50, false)))

                    log("cm_pills", tostring(robot.chem_master_create_pills(master, 5, 2, "test")))

                    local final = robot.chem_master_status(master)
                    log("cm_final", tostring(final.buffer.volume), tostring(final.output_items))

                    robot.wait(0.6)
                    local bottleMaster = robot.find("Bottle Master", 4)
                    if not bottleMaster then
                        log("cm_bottle_master", "missing")
                        return
                    end

                    log("cm_bottle_transfer",
                        tostring(robot.chem_master_transfer(bottleMaster, "Cryoxadone", 30, false)))
                    robot.wait(0.6)
                    log("cm_bottle", tostring(robot.chem_master_bottle(bottleMaster, 10, "bottle")))
                    local bottleFinal = robot.chem_master_status(bottleMaster)
                    log("cm_bottle_final", tostring(bottleFinal.output.volume),
                        tostring(bottleFinal.buffer.volume))
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("cm_status\ttransfer\t20\t0"), log);
                Assert.That(log, Does.Contain("cm_transfer\ttrue"), log);
                Assert.That(log, Does.Contain("cm_pills\ttrue"), log);
                Assert.That(log, Does.Contain("cm_final\t40\t2"), log);
                Assert.That(log, Does.Contain("cm_bottle_transfer\ttrue"), log);
                Assert.That(log, Does.Contain("cm_bottle\ttrue"), log);
                Assert.That(log, Does.Contain("cm_bottle_final\t10\t20"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
