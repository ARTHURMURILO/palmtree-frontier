using System.Collections.Generic;
using System.Linq;
using Content.Shared._NF.Research.Prototypes;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Guards the research-to-lathe wiring for the ported atmos machines. A technology's recipe
///     unlocks have to be reachable either from a lathe's dynamic packs (print directly once
///     researched) or from a blueprint pack (print the blueprint, feed it to a lathe).
/// </summary>
[TestFixture]
public sealed class FunkyResearchReachabilityTest
{
    // The HFR parts promised by the guidebook and the instruction paper: research the node, then
    // print the boxes from the engineering techfab.
    private static readonly string[] HfrRecipes =
    {
        "HFRCoreBox", "HFRCornerBox", "HFRFuelBox", "HFRModeratorBox", "HFRWasteBox", "HFRConsoleBox",
    };

    [Test]
    public async Task TechnologyUnlocksAreReachableFromLathePacks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var compFact = server.ResolveDependency<IComponentFactory>();
        var latheSystem = entMan.System<SharedLatheSystem>();

        await server.WaitAssertion(() =>
        {
            var latheRecipes = new HashSet<ProtoId<LatheRecipePrototype>>();
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || pair.IsTestPrototype(proto))
                    continue;

                if (!proto.TryGetComponent<LatheComponent>(out var lathe, compFact))
                    continue;

                latheSystem.AddRecipesFromPacks(latheRecipes, lathe.DynamicPacks);

                if (proto.TryGetComponent<EmagLatheRecipesComponent>(out var emag, compFact))
                    latheSystem.AddRecipesFromPacks(latheRecipes, emag.EmagDynamicPacks);
            }

            var blueprintRecipes = new HashSet<ProtoId<LatheRecipePrototype>>();
            foreach (var blueprint in protoMan.EnumeratePrototypes<BlueprintPrototype>())
            {
                foreach (var packId in blueprint.Packs)
                {
                    if (protoMan.TryIndex(packId, out LatheRecipePackPrototype? pack))
                        blueprintRecipes.UnionWith(pack.Recipes);
                }
            }

            var unreachable = new List<string>();
            foreach (var tech in protoMan.EnumeratePrototypes<TechnologyPrototype>())
            {
                foreach (var recipe in tech.RecipeUnlocks)
                {
                    if (latheRecipes.Contains(recipe) || blueprintRecipes.Contains(recipe))
                        continue;

                    unreachable.Add($"{recipe} (tech {tech.ID})");
                }
            }

            Assert.That(unreachable, Is.Empty,
                "these research unlocks cannot be printed from any lathe or blueprint pack: " +
                string.Join(", ", unreachable));

            // The HFR node must be printable directly at the engineering techfab, and keep the
            // blueprint route as well.
            foreach (var recipe in HfrRecipes)
            {
                Assert.That(latheRecipes, Does.Contain(recipe),
                    $"{recipe} is not in an engineering techfab dynamic pack, so post-research printing does not work");
                Assert.That(blueprintRecipes, Does.Contain(recipe),
                    $"{recipe} is not in a blueprint pack, so its blueprint cannot be printed");
            }
        });

        await pair.CleanReturnAsync();
    }
}
