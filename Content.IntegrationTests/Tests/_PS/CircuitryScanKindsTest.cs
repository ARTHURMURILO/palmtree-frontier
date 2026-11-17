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
public sealed class CircuitryScanKindsTest
{
    [Test]
    public async Task ScanItemsExcludesAnchoredStructures()
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
            entMan.SpawnEntity("Wrench", map.GridCoords.Offset(new Vector2(1f, 0f)));
            entMan.SpawnEntity("GasPipeStraight", map.GridCoords.Offset(new Vector2(0f, 1f)));
            entMan.SpawnEntity("GasVentScrubber", map.GridCoords.Offset(new Vector2(-1f, 0f)));
            entMan.SpawnEntity("ChemDispenser", map.GridCoords.Offset(new Vector2(0f, -1f)));

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local items = robot.scan_items(4)
                    for _, entry in ipairs(items) do
                        log("item", entry.name, entry.kind)
                    end

                    robot.wait(0.3)
                    local structures = robot.scan_structures(4)
                    for _, entry in ipairs(structures) do
                        log("structure", entry.name, entry.kind)
                    end

                    log("nearest", tostring(robot.nearest_structure(4) ~= nil))
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
                Assert.That(log, Does.Contain("item\twrench\titem"), log);
                Assert.That(log, Does.Not.Contain("item\tgas pipe"), log);
                Assert.That(log, Does.Not.Contain("item\tair scrubber"), log);
                Assert.That(log, Does.Not.Contain("item\tchemical dispenser"), log);
                Assert.That(log, Does.Contain("structure\tgas pipe\tstructure"), log);
                Assert.That(log, Does.Contain("structure\tair scrubber\tstructure"), log);
                Assert.That(log, Does.Contain("structure\tchemical dispenser\tstructure"), log);
                Assert.That(log, Does.Contain("nearest\ttrue"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
