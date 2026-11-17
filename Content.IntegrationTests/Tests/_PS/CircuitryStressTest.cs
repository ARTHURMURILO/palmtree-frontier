#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryStressTest
{
    [Test]
    public async Task ManyRunningAssembliesStayHealthy()
    {
        const int assemblyCount = 16;

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var tileMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();

        var assemblies = new List<EntityUid>(assemblyCount);

        await server.WaitAssertion(() =>
        {
            var plating = new Tile(tileMan["Plating"].TileId);
            var grid = map.Grid;

            for (var i = 0; i < assemblyCount; i++)
            {
                var x = i % 4;
                var y = i / 4;
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), plating);

                var assembly = entMan.SpawnEntity("CircuitAssemblyFilled",
                    new EntityCoordinates(grid.Owner, new Vector2(x + 0.5f, y + 0.5f)));
                assemblies.Add(assembly);

                var component = entMan.GetComponent<CircuitAssemblyComponent>(assembly);
                var cpu = entMan.GetComponent<CircuitCpuComponent>(component.Cpu!.Value);
                cpu.Source = """
                    function main()
                        while true do
                            robot.wait(1)
                            log("tick")
                        end
                    end
                    """;
            }
        });

        await pair.RunTicksSync(4);

        var brain = entMan.System<LuaBrainSystem>();
        await server.WaitPost(() =>
        {
            foreach (var assembly in assemblies)
                Assert.That(brain.Start(assembly), Is.True, $"Failed to start {entMan.ToPrettyString(assembly)}");
        });

        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            foreach (var assembly in assemblies)
            {
                var runtime = brain.GetRuntime(assembly);
                Assert.That(runtime, Is.Not.Null, $"Missing runtime for {entMan.ToPrettyString(assembly)}");
                Assert.That(runtime!.Program.LastError, Is.Null, runtime.Program.LastError);
                Assert.That(runtime.Program.Log, Is.Not.Empty, "A running assembly should have logged at least one tick.");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var assembly in assemblies)
                brain.Stop(assembly);
        });

        await pair.CleanReturnAsync();
    }
}
