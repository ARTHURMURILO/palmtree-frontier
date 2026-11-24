// SPDX-FileCopyrightText: 2026 Palmtree Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._Funkystation.ConstructionChalk;
using Content.Client._Funkystation.Placement;
using Content.Client.Construction;
using Content.Shared.Construction.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: <see cref="Content.Client.Atmos.AlignAtmosPipeLayers"/> switches the placed pipe layer by
/// rebuilding the active placement hijack through <see cref="IAtmosPipeLayerHijack.WithPrototype"/>.
/// A hijack that stops implementing the interface, or whose WithPrototype returns the wrong kind of
/// hijack, silently falls back to the empty branch and pipe layers stop switching. Both hijack types
/// that can be active for an atmos pipe recipe (construction menu and construction chalk) are checked
/// here, together with the alternative-prototype data the mode indexes into.
/// </summary>
[TestFixture]
public sealed class AtmosPipeLayerHijackTest
{
    private const string PipeRecipe = "GasPipeStraight";

    [Test]
    public async Task PipeLayerHijacksPreservePrototypeSwitching()
    {
        // A connected pair is needed to have the client entity systems (including the client
        // ConstructionSystem the hijack holds) initialized.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var prototypes = client.ProtoMan;
            var construction = client.System<ConstructionSystem>();

            Assert.That(prototypes.TryIndex<ConstructionPrototype>(PipeRecipe, out var recipe), Is.True,
                $"{PipeRecipe} should exist as a construction recipe");

            Assert.That(recipe!.PlacementMode, Is.EqualTo("AlignAtmosPipeLayers"),
                $"{PipeRecipe} should use the pipe-layer placement mode");

            var alternatives = recipe.AlternativePrototypes;
            Assert.That(alternatives.Length, Is.GreaterThan(1),
                "The pipe-layer mode needs at least a primary and one alt prototype");

            // AlignAtmosPipeLayers indexes this array by layer and silently no-ops when a prototype
            // fails to resolve, so verify the data it relies on.
            ConstructionPrototype? secondary = null;
            foreach (var altId in alternatives)
            {
                Assert.That(prototypes.TryIndex(altId, out ConstructionPrototype? alt), Is.True,
                    $"Alternative prototype {altId} should resolve");

                Assert.Multiple(() =>
                {
                    Assert.That(alt!.Type, Is.EqualTo(ConstructionType.Structure),
                        $"Alternative prototype {altId} should be a structure");
                    Assert.That(alt.PlacementMode, Is.EqualTo(recipe.PlacementMode),
                        $"Alternative prototype {altId} should use the same placement mode");
                });

                secondary ??= alt;
            }

            Assert.That(secondary, Is.Not.Null);

            // Construction menu placement.
            var menuHijack = new ConstructionPlacementHijack(construction, recipe);
            Assert.That(menuHijack, Is.InstanceOf<IAtmosPipeLayerHijack>(),
                "ConstructionPlacementHijack must implement IAtmosPipeLayerHijack");

            var menuSwitched = ((IAtmosPipeLayerHijack) menuHijack).WithPrototype(secondary!);
            Assert.Multiple(() =>
            {
                Assert.That(menuSwitched, Is.InstanceOf<ConstructionPlacementHijack>(),
                    "WithPrototype must keep returning a hijack AlignAtmosPipeLayers can rebuild");
                Assert.That(((IAtmosPipeLayerHijack) menuSwitched).CurrentPrototype?.ID,
                    Is.EqualTo(secondary!.ID),
                    "WithPrototype must swap the construction prototype");
            });

            // Chalk placement: the chalk has no ghost ack, so a fresh container name is used instead
            // and the hijack must expose the same switching contract.
            var chalkHijack = new ChalkPlacementHijack(
                client.EntMan,
                prototypes,
                EntityUid.Invalid,
                recipe,
                (_, _, _, _) => { });

            Assert.That(chalkHijack, Is.InstanceOf<IAtmosPipeLayerHijack>(),
                "ChalkPlacementHijack must implement IAtmosPipeLayerHijack");

            var chalkSwitched = ((IAtmosPipeLayerHijack) chalkHijack).WithPrototype(secondary!);
            Assert.Multiple(() =>
            {
                Assert.That(chalkSwitched, Is.InstanceOf<ChalkPlacementHijack>(),
                    "WithPrototype must keep returning a chalk hijack");
                Assert.That(((IAtmosPipeLayerHijack) chalkSwitched).CurrentPrototype?.ID,
                    Is.EqualTo(secondary!.ID),
                    "WithPrototype must swap the chalk's construction prototype");
            });
        });

        await pair.CleanReturnAsync();
    }
}
