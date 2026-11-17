using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Reactions;
using Content.Shared._Funkystation.Atmos.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Funkystation.Atmos;

/// <summary>
/// Structural checks for the Funky atmos port: the gas table, the crystallizer recipes and the
/// filled canisters must all agree with the Gas enum in <see cref="Atmospherics"/>.
/// </summary>
[TestFixture]
public sealed class FunkyAtmosPrototypesTest
{
    private static readonly (EntProtoId Canister, Gas Gas)[] NewGasCanisters =
    {
        ("BZCanister", Gas.BZ),
        ("HealiumCanister", Gas.Healium),
        ("NitriumCanister", Gas.Nitrium),
        ("PluoxiumCanister", Gas.Pluoxium),
        ("HydrogenCanister", Gas.Hydrogen),
        ("HyperNobliumCanister", Gas.HyperNoblium),
        ("ProtoNitrateCanister", Gas.ProtoNitrate),
        ("ZaukerCanister", Gas.Zauker),
        ("HalonCanister", Gas.Halon),
        ("AntiNobliumCanister", Gas.AntiNoblium),
    };

    /// <summary>
    /// Every id up to <see cref="Atmospherics.TotalNumberOfGases"/> must have a gas prototype, and
    /// the prototype count must not exceed the table size. This is the check that catches a merge
    /// leaving two gases on the same id.
    /// </summary>
    [Test]
    public async Task GasTableMatchesEnum()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        Assert.Multiple(() =>
        {
            Assert.That(prototypes.Count<GasPrototype>(), Is.EqualTo(Atmospherics.TotalNumberOfGases));

            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            {
                Assert.That(prototypes.HasIndex<GasPrototype>(i.ToString()),
                    Is.True,
                    $"No gas prototype for id {i} ({(Gas) i})");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every crystallizer recipe must fit in the gas mixture array and every product id must
    /// resolve to an entity prototype. Recipes are referenced from C# by string id, so a missing
    /// product prototype is not caught by the YAML linter.
    /// </summary>
    [Test]
    public async Task CrystallizerRecipesAreValid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        Assert.Multiple(() =>
        {
            foreach (var recipe in prototypes.EnumeratePrototypes<CrystallizerRecipePrototype>())
            {
                var requirements = recipe.MinimumRequirements;

                Assert.That(requirements, Is.Not.Empty, $"{recipe.ID} has no gas requirements");
                Assert.That(requirements.Length, Is.EqualTo(Atmospherics.TotalNumberOfGases),
                    $"{recipe.ID} does not have one slot per gas");
                Assert.That(requirements.Any(moles => moles > 0f), Is.True,
                    $"{recipe.ID} can never be completed");
                Assert.That(recipe.MaximumTemperature, Is.GreaterThan(recipe.MinimumTemperature),
                    $"{recipe.ID} has an empty temperature range");
                Assert.That(recipe.Products, Is.Not.Empty, $"{recipe.ID} has no products");

                foreach (var (product, amount) in recipe.Products)
                {
                    Assert.That(prototypes.HasIndex<EntityPrototype>(product), Is.True,
                        $"{recipe.ID} spawns unknown prototype {product}");
                    Assert.That(amount, Is.GreaterThan(0), $"{recipe.ID} spawns {product} zero times");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// HydrogenFire used to be a truncated 14-entry array with the 0.01 requirement on the
    /// nitrium slot, so it could never fire. Keep it on the hydrogen slot.
    /// </summary>
    [Test]
    public async Task HydrogenFireUsesHydrogenSlot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        var fire = prototypes.Index<GasReactionPrototype>("HydrogenFire");
        var requirements = fire.MinimumRequirements;

        Assert.Multiple(() =>
        {
            Assert.That(requirements, Has.Length.EqualTo(Atmospherics.TotalNumberOfGases));
            Assert.That(requirements[(int) Gas.Hydrogen], Is.GreaterThan(0f),
                "HydrogenFire does not require hydrogen");
            Assert.That(requirements[(int) Gas.Oxygen], Is.GreaterThan(0f),
                "HydrogenFire does not require oxygen");
            Assert.That(requirements[(int) Gas.Nitrium], Is.Zero,
                "HydrogenFire still requires nitrium");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The filled canisters must contain their gas at the same volume as the rest of the codebase's
    /// canisters, otherwise their pressure silently differs from Funky's.
    /// </summary>
    [Test]
    public async Task NewGasCanistersAreFilled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

        var mixtures = new List<(EntProtoId Canister, Gas Gas, float Moles, float TotalMoles, float Volume)>();

        await server.WaitPost(() =>
        {
            foreach (var (canisterId, gas) in NewGasCanisters)
            {
                var uid = entMan.SpawnEntity(canisterId.Id, coords);
                var canister = entMan.GetComponent<GasCanisterComponent>(uid);
                mixtures.Add((canisterId, gas,
                    canister.Air.GetMoles(gas),
                    canister.Air.TotalMoles,
                    canister.Air.Volume));
            }
        });

        Assert.Multiple(() =>
        {
            foreach (var (canisterId, gas, moles, totalMoles, volume) in mixtures)
            {
                Assert.That(moles, Is.GreaterThan(0f),
                    $"{canisterId} does not contain {gas}");
                Assert.That(totalMoles, Is.EqualTo(moles).Within(0.01f),
                    $"{canisterId} contains gases other than {gas}");
                Assert.That(volume, Is.EqualTo(1500f),
                    $"{canisterId} does not use the standard canister volume");
            }
        });

        await pair.CleanReturnAsync();
    }
}
