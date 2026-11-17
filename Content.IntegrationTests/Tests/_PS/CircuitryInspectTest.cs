#nullable enable
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryInspectTest
{
    [Test]
    public async Task InspectReportsIdentityAndClothing()
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
        EntityUid hat = default;
        EntityUid idCard = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            cell = entMan.SpawnEntity("PowerCellSmall", map.GridCoords);
            sensor = entMan.SpawnEntity("CircuitSensor", map.GridCoords);
            human = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            hat = entMan.SpawnEntity("ClothingHeadHatBeret", map.GridCoords);
            idCard = entMan.SpawnEntity("PassengerIDCard", map.GridCoords);

        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            server.System<MetaDataSystem>().SetEntityName(human, "Test Human");

            var id = entMan.GetComponent<IdCardComponent>(idCard);
            id.FullName = "Alice";
            id.LocalizedJobTitle = "Deputy";
            id.JobPrototype = "Deputy";
            entMan.EnsureComponent<AccessComponent>(idCard).Tags.Add("Security");

            var inventory = entMan.System<InventorySystem>();
            Assert.Multiple(() =>
            {
                Assert.That(inventory.TryEquip(human, hat, "head", silent: true, force: true), Is.True);
                Assert.That(inventory.TryEquip(human, idCard, "id", silent: true, force: true), Is.True);
            });

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
                Assert.That(slots.TryInsert(assembly, "sensor", sensor, null), Is.True);
            });

            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = """
                function main()
                    log("sensor", tostring(robot.has_module("sensor")))
                    local things = robot.scan(4)
                    if type(things) == "table" then
                        log("scan", tostring(#things))
                        for _, thing in ipairs(things) do
                            log("thing", thing.name, thing.kind)
                        end
                    else
                        log("scan failed")
                    end
                    local mob = robot.find("Test Human", 4)
                    if not mob then
                        log("no mob")
                        return
                    end
                    log("job", tostring(robot.has_job(mob, "Deputy")), tostring(robot.has_job(mob, "Pirate")))
                    log("access", tostring(robot.has_access(mob, "Security")), tostring(robot.has_access(mob, "Pirate")))

                    local who = robot.inspect(mob)
                    local hasSecurityTag = false
                    for _, tag in ipairs(who.id.access) do
                        if tag == "Security" then
                            hasSecurityTag = true
                        end
                    end
                    log("inspect", who.name, who.id.name, who.id.job, who.id.job_id,
                        tostring(hasSecurityTag), tostring(who.wearing.head))
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
                Assert.That(log, Does.Contain("job\ttrue\tfalse"), log);
                Assert.That(log, Does.Contain("access\ttrue\tfalse"), log);
                Assert.That(log, Does.Contain("inspect\tTest Human\tAlice\tDeputy\tDeputy\ttrue"), log);
            });
        });

        await pair.CleanReturnAsync();
    }
}
