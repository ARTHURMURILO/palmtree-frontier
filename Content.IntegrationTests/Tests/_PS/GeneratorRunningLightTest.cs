using System.Numerics;
using Content.Server.Power.Generator;
using Content.Shared._PS.Power;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Power.Generator;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the B.L.Y.A.T.M.A.N. generator's purple cone light only lights while the generator runs.
/// </summary>
[TestFixture]
public sealed class GeneratorRunningLightTest
{
    [Test]
    public async Task LightOnlyLightsWhileRunning()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid generator = default;

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            var lights = server.System<SharedPointLightSystem>();
            var transform = server.System<SharedTransformSystem>();
            var generators = server.System<GeneratorSystem>();

            generator = entMan.SpawnEntity("PortableGeneratorBlyatman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            transform.AnchorEntity(generator);

            Assert.That(entMan.HasComponent<GeneratorRunningLightComponent>(generator), Is.True,
                "The Blyatman generator should sync its light to its running state.");
            Assert.That(lights.TryGetLight(generator, out var light), Is.True, "It should have its point light.");
            Assert.That(light!.Enabled, Is.False, "The light starts off.");

            // Fill the tank with vodka and start the generator.
            Assert.That(solutions.TryGetSolution(generator, "tank", out var tank, out _), Is.True);
            Assert.That(solutions.TryAddReagent(tank!.Value, "Vodka", FixedPoint2.New(100), out _), Is.True);
            generators.SetFuelGeneratorOn(generator, true);
            Assert.That(entMan.GetComponent<FuelGeneratorComponent>(generator).On, Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var lights = server.System<SharedPointLightSystem>();
            Assert.That(lights.TryGetLight(generator, out var light), Is.True);
            Assert.That(light!.Enabled, Is.True, "The light should be lit while the generator runs.");

            server.System<GeneratorSystem>().SetFuelGeneratorOn(generator, false);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var lights = server.System<SharedPointLightSystem>();
            Assert.That(lights.TryGetLight(generator, out var light), Is.True);
            Assert.That(light!.Enabled, Is.False, "The light should go out when the generator stops.");

            entMan.DeleteEntity(generator);
        });

        await pair.CleanReturnAsync();
    }
}
