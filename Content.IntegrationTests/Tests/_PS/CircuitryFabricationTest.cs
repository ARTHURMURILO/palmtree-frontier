#nullable enable
using System.Linq;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryFabricationTest
{
    private static readonly string[] CircuitryRecipes =
    {
        "CircuitCpuBasic",
        "CircuitCpuAdvanced",
        "CircuitWheels",
        "CircuitTracks",
        "CircuitThruster",
        "CircuitManipulator",
        "CircuitSpeaker",
        "CircuitSensor",
        "CircuitRadio",
        "CircuitBotanicalAnalyzer",
        "CircuitChemicalAnalyzer",
        "CircuitMedicalAnalyzer",
        "CircuitGasAnalyzer",
    };

    [Test]
    public async Task EveryCircuitryModuleHasAnExosuitRecipeAndResearchUnlock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.That(protoMan.HasIndex<TechnologyPrototype>("NFCircuitry"), Is.True);
            Assert.That(protoMan.HasIndex<TechnologyPrototype>("NFAdvancedCircuitry"), Is.True);
            Assert.That(protoMan.HasIndex<LatheRecipePackPrototype>("CircuitryParts"), Is.True);

            var basic = protoMan.Index<TechnologyPrototype>("NFCircuitry");
            var advanced = protoMan.Index<TechnologyPrototype>("NFAdvancedCircuitry");
            var pack = protoMan.Index<LatheRecipePackPrototype>("CircuitryParts");

            Assert.Multiple(() =>
            {
                Assert.That(basic.Position, Is.EqualTo(new Vector2i(0, 17)));
                Assert.That(advanced.Position, Is.EqualTo(new Vector2i(1, 17)));
                Assert.That(protoMan.Index<TechnologyPrototype>("NFVim").Position,
                    Is.EqualTo(new Vector2i(3, 17)));
                Assert.That(pack.Recipes, Is.EquivalentTo(CircuitryRecipes));
            });

            var unlocked = basic.RecipeUnlocks
                .Concat(advanced.RecipeUnlocks)
                .Select(id => id.Id)
                .ToHashSet();

            foreach (var recipe in CircuitryRecipes)
                Assert.That(unlocked, Contains.Item(recipe), $"Recipe {recipe} is not research-unlocked.");

            foreach (var recipe in CircuitryRecipes)
                Assert.That(protoMan.HasIndex<LatheRecipePrototype>(recipe), Is.True, $"Missing lathe recipe {recipe}.");

            // The chassis itself is deliberately admin-spawn only during the live test.
            Assert.That(protoMan.HasIndex<LatheRecipePrototype>("CircuitAssembly"), Is.False);

            var fabricator = entMan.SpawnEntity("ExosuitFabricator", map.GridCoords);
            var lathe = entMan.GetComponent<LatheComponent>(fabricator);
            Assert.That(lathe.DynamicPacks,
                Does.Contain(new ProtoId<LatheRecipePackPrototype>("CircuitryParts")));
        });

        await pair.CleanReturnAsync();
    }
}
