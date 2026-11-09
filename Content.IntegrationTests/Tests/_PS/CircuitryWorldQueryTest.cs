#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Server.Decals;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Decals;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryWorldQueryTest
{
    [Test]
    public async Task DecalsAndDoorStatusAreQueryable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;
        EntityUid cpu = default;
        uint decalId = 0;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            var cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            var sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);

            entMan.SpawnEntity("Airlock", map.GridCoords.Offset(new Vector2(1f, 0f)));
            entMan.EnsureComponent<DecalGridComponent>(map.GridCoords.EntityId);
            Assert.That(entMan.System<DecalSystem>().TryAddDecal(
                "Dirt",
                map.GridCoords,
                out decalId,
                cleanable: true), Is.True);

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    local doors = robot.scan_doors(4)
                    for _, entry in ipairs(doors) do
                        local status = robot.door_status(entry.id)
                        log("door", entry.name, status.state, tostring(status.bolted),
                            tostring(status.access), tostring(status.can_open))
                    end

                    robot.wait(0.3)
                    local decals = robot.decals(1)
                    log("decals", tostring(#decals), decals[1] and decals[1].decal or "none")
                    if decals[1] then
                        local decal = decals[1]
                        log("decalinfo", tostring(decal.decal_id), tostring(decal.cleanable), decal.x, decal.y)
                    end
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
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Program.LastError, Is.Null, log);
                Assert.That(log, Does.Contain("door\t"), log);
                Assert.That(log, Does.Contain("false"), log);
                Assert.That(log, Does.Contain("decals\t1"), log);
                Assert.That(log, Does.Contain("decalinfo\t" + decalId + "\ttrue\t"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
