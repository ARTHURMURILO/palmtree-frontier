using Content.Shared.Humanoid.Prototypes;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: every round-start organic species must have hunger and thirst. Synth is the only
/// intentional exception (it can eat, but never needs to); Skeleton is not round-start and has no
/// stomach to eat with.
/// </summary>
[TestFixture]
public sealed class SpeciesNutritionTest
{
    [Test]
    public async Task RoundStartSpeciesHaveNutrition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        foreach (var species in protoMan.EnumeratePrototypes<SpeciesPrototype>())
        {
            if (!species.RoundStart)
                continue;

            var mob = protoMan.Index<EntityPrototype>(species.Prototype);
            var hasHunger = mob.Components.ContainsKey("Hunger");
            var hasThirst = mob.Components.ContainsKey("Thirst");

            if (species.ID == "Synth")
            {
                Assert.Multiple(() =>
                {
                    Assert.That(hasHunger, Is.False, "Synth should not have Hunger.");
                    Assert.That(hasThirst, Is.False, "Synth should not have Thirst.");
                });
                continue;
            }

            Assert.Multiple(() =>
            {
                Assert.That(hasHunger, Is.True, $"{species.ID} is missing Hunger.");
                Assert.That(hasThirst, Is.True, $"{species.ID} is missing Thirst.");
            });
        }

        await pair.CleanReturnAsync();
    }
}
