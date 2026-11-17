#nullable enable
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Server._PS.Circuitry.UI;
using Content.Shared._PS.Circuitry;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryEditorTest
{
    [Test]
    public async Task RunStoresCurrentEditorSourceFirst()
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

            var slots = entMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(assembly, "cpu", cpu, null), Is.True);
            Assert.That(slots.TryInsert(assembly, "cell", cell, null), Is.True);
        });

        await pair.RunTicksSync(2);

        const string source = """
            function main()
                log("editor source")
            end
            """;

        var cpuUi = entMan.System<CircuitCpuUiSystem>();
        await server.WaitPost(() =>
        {
            var component = entMan.GetComponent<CircuitAssemblyComponent>(assembly);
            cpuUi.OnAssemblyRun(assembly, component, new CircuitCpuRunMessage(source));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var cpuComponent = entMan.GetComponent<CircuitCpuComponent>(cpu);
            var runtime = entMan.System<LuaBrainSystem>().GetRuntime(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(cpuComponent.Source, Is.EqualTo(source));
                Assert.That(runtime, Is.Not.Null);
                Assert.That(runtime!.Program.Log, Does.Contain("editor source"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
