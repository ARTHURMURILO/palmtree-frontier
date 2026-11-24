// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Funkystation.Atmos.Components;
using Content.Server._Funkystation.Atmos.HFR.Systems;
using Content.Shared._Funkystation.Atmos.HFR;
using Content.Shared._Funkystation.Atmos.Prototypes;
using Content.Shared.Atmos;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Guards the HFR's fork temperature scale: the atmos ceiling stays at the upstream 262,144 K and
///     the fusion math is scaled to fit under it instead of raising <see cref="Atmospherics.Tmax"/>.
/// </summary>
[TestFixture]
public sealed class HFRTemperatureScaleTest
{
    // Default atmos device update interval: the full atmosphere cycle is 10 processing states at the
    // default 15 Hz, so a device is updated once per 10/15 seconds.
    private const float DeviceDt = 10f / 15f;

    [Test]
    public async Task TmaxStaysAtTheUpstreamCeiling()
    {
        await using var pair = await PoolManager.GetServerClient();

        Assert.Multiple(() =>
        {
            Assert.That(Atmospherics.Tmax, Is.EqualTo(262144f),
                "the fork-wide atmos temperature ceiling was raised again; the HFR must scale instead");
            Assert.That(Atmospherics.TotalNumberOfGases, Is.EqualTo(21),
                "the 10 new /tg/ gases were dropped");

            Assert.That(HypertorusFusionReactor.FusionMaximumTemperature, Is.LessThanOrEqualTo(Atmospherics.Tmax),
                "the fusion ceiling is above the atmos clamp, so fusion temperatures would be silently capped");
            Assert.That(HypertorusFusionReactor.FusionMaximumTemperature, Is.GreaterThan(0f));
            Assert.That(HypertorusFusionReactor.TemperatureScale,
                Is.EqualTo(Atmospherics.Tmax / 2.5e7f).Within(1e-7f),
                "the fusion temperature scale factor does not match the fork's atmos ceiling");

            // Ambient air must be power level 0 (a running but cold reactor) and the ceiling level 6.
            Assert.That(HypertorusFusionReactor.GetPowerLevel(Atmospherics.T20C), Is.Zero,
                "ambient air is already above power level 0");
            Assert.That(HypertorusFusionReactor.GetPowerLevel(HypertorusFusionReactor.FusionMaximumTemperature),
                Is.EqualTo(6));

            var boundaries = HypertorusFusionReactor.PowerLevelUpperTemperatures;
            Assert.That(boundaries, Has.Length.EqualTo(6), "expected one upper boundary per power level 0-5");
            Assert.That(boundaries[0], Is.EqualTo(500f), "level 0 must still end at the old 500 K boundary");
            for (var i = 1; i < boundaries.Length; i++)
                Assert.That(boundaries[i], Is.GreaterThan(boundaries[i - 1]), $"boundary {i} is not increasing");
            Assert.That(boundaries[^1], Is.LessThan(HypertorusFusionReactor.FusionMaximumTemperature),
                "power level 6 must start below the fusion ceiling");

            // The cold coolant healing threshold is another absolute temperature that has to scale.
            Assert.That(HypertorusFusionReactor.HypertorusColdCoolantThreshold, Is.GreaterThan(1000f));
            Assert.That(HypertorusFusionReactor.HypertorusColdCoolantThreshold, Is.LessThan(1100f),
                "the cold coolant threshold did not follow the temperature scale");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Runs the real fusion process with a continuous fuel feed until the mix stops heating, and
    ///     checks that it reaches the scaled ceiling without ever running into the atmos clamp.
    /// </summary>
    [Test]
    public async Task FusionReachesTheScaledCeiling()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();

        await server.WaitAssertion(() =>
        {
            var coreUid = entMan.SpawnEntity("HFRCore", testMap.GridCoords);
            var core = entMan.GetComponent<HFRCoreComponent>(coreUid);
            var recipe = protoMan.Index<FusionRecipePrototype>("h2T2FuelRecipe");
            FillCoreForRecipe(core, recipe);

            // Enough device updates for the mix to heat through all six power levels.
            for (var i = 0; i < 8000; i++)
            {
                // Approximate a continuous fuel feed: keep the required gases at their kickstart level.
                foreach (var gas in recipe.Requirements)
                    core.InternalFusion!.SetMoles(Enum.Parse<Gas>(gas), 1000f);

                hfr.FusionProcess(coreUid, core, DeviceDt);

                if (core.FusionTemperature >= HypertorusFusionReactor.FusionMaximumTemperature * recipe.TemperatureChangeMultiplier)
                    break;
            }

            var expectedMaximum = HypertorusFusionReactor.FusionMaximumTemperature * recipe.TemperatureChangeMultiplier;
            Assert.Multiple(() =>
            {
                Assert.That(float.IsFinite(core.FusionTemperature), Is.True,
                    $"the fusion temperature became {core.FusionTemperature}");
                Assert.That(core.PowerLevel, Is.EqualTo(6),
                    $"the fusion reaction stalled at power level {core.PowerLevel} ({core.FusionTemperature} K)");
                Assert.That(core.FusionTemperature, Is.GreaterThan(expectedMaximum * 0.99f),
                    $"the fusion mix stopped at {core.FusionTemperature} K instead of {expectedMaximum} K");
                Assert.That(core.FusionTemperature, Is.LessThanOrEqualTo(Atmospherics.Tmax),
                    "the fusion mix exceeded the atmos temperature ceiling");
            });

            entMan.DeleteEntity(coreUid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The same amount of heat must produce the same temperature rise per second, no matter how
    ///     long the atmos device update interval is. The old code added the heat once per event.
    /// </summary>
    [Test]
    public async Task HeatIsAppliedPerSecond()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();

        await server.WaitAssertion(() =>
        {
            var recipe = protoMan.Index<FusionRecipePrototype>("h2T2FuelRecipe");

            var firstUid = entMan.SpawnEntity("HFRCore", testMap.GridCoords);
            var first = entMan.GetComponent<HFRCoreComponent>(firstUid);
            FillCoreForRecipe(first, recipe);

            var secondUid = entMan.SpawnEntity("HFRCore", testMap.GridCoords.Offset(new System.Numerics.Vector2(3f, 0f)));
            var second = entMan.GetComponent<HFRCoreComponent>(secondUid);
            FillCoreForRecipe(second, recipe);

            // A warm-up update gives both cores the same (non-zero) energy state.
            hfr.FusionProcess(firstUid, first, DeviceDt);
            hfr.FusionProcess(secondUid, second, DeviceDt);

            var firstStart = first.InternalFusion!.Temperature;
            var secondStart = second.InternalFusion!.Temperature;

            hfr.FusionProcess(firstUid, first, 0.2f);
            hfr.FusionProcess(secondUid, second, 0.4f);

            var shortRise = first.InternalFusion!.Temperature - firstStart;
            var longRise = second.InternalFusion!.Temperature - secondStart;

            Assert.Multiple(() =>
            {
                Assert.That(shortRise, Is.GreaterThan(0f), "the fusion heat output was not positive");
                Assert.That(longRise / shortRise, Is.EqualTo(2f).Within(0.05f),
                    $"doubling the update interval changed the applied heat by {longRise / shortRise:0.###}x instead of 2x");
            });

            entMan.DeleteEntity(firstUid);
            entMan.DeleteEntity(secondUid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Fusion recipes are referenced by gas name from YAML and parsed inside the atmos tick. A
    ///     malformed recipe must be caught here instead of throwing in the live game.
    /// </summary>
    [Test]
    public async Task FusionRecipesUseRealGases()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        var failures = new List<string>();
        foreach (var recipe in prototypes.EnumeratePrototypes<FusionRecipePrototype>())
        {
            CheckGasIds(failures, recipe, "requirements", recipe.Requirements);
            CheckGasIds(failures, recipe, "primaryProducts", recipe.PrimaryProducts);
            CheckGasIds(failures, recipe, "secondaryProducts", recipe.SecondaryProducts);

            if (recipe.Requirements.Count < 2)
                failures.Add($"{recipe.ID} needs a fuel pair, has {recipe.Requirements.Count} requirement(s)");
            if (recipe.PrimaryProducts.Count < 1)
                failures.Add($"{recipe.ID} has no primary product");
            if (recipe.SecondaryProducts.Count < 6)
                failures.Add($"{recipe.ID} needs at least six secondary products, has {recipe.SecondaryProducts.Count}");
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));

        await pair.CleanReturnAsync();
    }

    private static void CheckGasIds(List<string> failures, FusionRecipePrototype recipe, string field, List<string> gasIds)
    {
        foreach (var gasId in gasIds)
        {
            if (!HypertorusFusionReactor.TryParseGas(gasId, out var gas))
            {
                failures.Add($"{recipe.ID}.{field} has non-gas id '{gasId}'");
                continue;
            }

            if ((int) gas < 0 || (int) gas >= Atmospherics.TotalNumberOfGases)
                failures.Add($"{recipe.ID}.{field} has out-of-range gas id '{gasId}'");
        }
    }

    /// <summary>
    ///     Fills a core with a large-enough fuel mix to keep the reaction running for the duration of
    ///     a test, plus a small plasma/nitrogen moderator.
    /// </summary>
    internal static void FillCoreForRecipe(HFRCoreComponent core, FusionRecipePrototype recipe)
    {
        core.SelectedRecipeId = recipe.ID;

        core.InternalFusion = new GasMixture(HypertorusFusionReactor.InternalFusionVolume) { Temperature = Atmospherics.T20C };
        foreach (var gas in recipe.Requirements)
            core.InternalFusion.SetMoles(Enum.Parse<Gas>(gas), 1000f);

        core.ModeratorInternal = new GasMixture(HypertorusFusionReactor.ModeratorInternalVolume) { Temperature = Atmospherics.T20C };
        core.ModeratorInternal.SetMoles(Gas.Plasma, 500f);
        core.ModeratorInternal.SetMoles(Gas.Nitrogen, 250f);
    }
}
