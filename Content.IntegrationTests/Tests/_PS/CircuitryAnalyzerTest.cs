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
public sealed class CircuitryAnalyzerTest
{
    [Test]
    public async Task AnalyzerModulesExposeGameData()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid cpu = default;
        EntityUid cell = default;
        EntityUid sensor = default;
        EntityUid botany = default;
        EntityUid chemistry = default;
        EntityUid medical = default;
        EntityUid manipulator = default;
        EntityUid seeds = default;
        EntityUid beaker = default;
        EntityUid chemBeaker = default;
        EntityUid human = default;
        EntityUid dispenser = default;
        EntityUid puddle = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            botany = entMan.SpawnEntity("CircuitBotanicalAnalyzer", map.GridCoords);
            chemistry = entMan.SpawnEntity("CircuitChemicalAnalyzer", map.GridCoords);
            medical = entMan.SpawnEntity("CircuitMedicalAnalyzer", map.GridCoords);
            manipulator = entMan.SpawnEntity("CircuitManipulator", map.GridCoords);
            seeds = entMan.SpawnEntity("WheatSeeds", map.GridCoords.Offset(new Vector2(1f, 0f)));
            beaker = entMan.SpawnEntity("Beaker", map.GridCoords.Offset(new Vector2(-1f, 0f)));
            chemBeaker = entMan.SpawnEntity("Beaker", map.GridCoords.Offset(new Vector2(-1f, 1f)));
            human = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0f, 1f)));
            dispenser = entMan.SpawnEntity("ChemDispenser", map.GridCoords.Offset(new Vector2(1f, 1f)));
            puddle = entMan.SpawnEntity("PuddleVomit", map.GridCoords.Offset(new Vector2(0f, -1f)));

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "analyzer1", botany, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "analyzer2", chemistry, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "analyzer3", medical, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "left_arm", manipulator, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "beaker1", beaker, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local seed = robot.find("wheat", 4)
                    if seed then
                        local plant = robot.analyze_plant(seed)
                        log("plant", plant.seed.name, tostring(plant.seed.yield))
                    else
                        log("plant", "missing")
                    end

                    local person = robot.nearest_person(4)
                    if person then
                        local med = robot.analyze_medical(person)
                        log("medical", med.state, tostring(med.health), tostring(med.dead))
                    else
                        log("medical", "missing")
                    end

                    local container = robot.find("beaker", 4)
                    if container then
                        local chem = robot.analyze_chemicals(container)
                        log("chem", tostring(#chem.solutions))
                    else
                        log("chem", "missing")
                    end

                    local machine = robot.find("chemical dispenser", 4)
                    if machine then
                        log("dispense", tostring(robot.dispense(machine, 1, 10, 1)))
                        log("dispensed_volume", tostring(robot.reagents(1).volume))
                    else
                        log("dispense", "missing")
                    end

                    robot.wait(0.3)
                    local puddles, puddleError = robot.scan_puddles(2)
                    log("puddles", puddles and tostring(#puddles) or tostring(puddleError))
                    if puddles and puddles[1] then
                        local spill = puddles[1]
                        log("puddle", spill.name, spill.solution, tostring(spill.volume),
                            spill.contents[1].id, tostring(spill.contents[1].amount))
                    end
                end
                """;
        });

        await pair.RunTicksSync(2);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() => Assert.That(brain.Start(assembly), Is.True));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var runtime = brain.GetRuntime(assembly);
            Assert.That(runtime, Is.Not.Null);

            var log = string.Join("\n", runtime!.Program.Log);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("plant\twheat"), log);
                Assert.That(log, Does.Contain("medical\tAlive"), log);
                Assert.That(log, Does.Contain("chem\t1"), log);
                Assert.That(log, Does.Contain("dispense\ttrue"), log);
                Assert.That(log, Does.Contain("dispensed_volume\t"), log);
                Assert.That(log, Does.Contain("puddles\t1"), log);
                Assert.That(log, Does.Contain("puddle\tpuddle\tpuddle\t10\tVomit\t10"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
