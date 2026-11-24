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
///     Guards the HFR's heat-up time per power level. The fork temperature scale (262,144 K instead of
///     Funky's 25,000,000 K) compresses the power level boundaries, so the heat rates have to stay in
///     the same absolute range to keep the reactor taking a similar time to spool up.
/// </summary>
[TestFixture]
public sealed class HFRHeatUpCurveTest
{
    // Default atmos device update interval: 10 processing states at the default 15 Hz.
    private const float DeviceDt = 10f / 15f;

    [Test]
    public async Task HeatUpTimePerPowerLevelStaysInRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();

        await server.WaitAssertion(() =>
        {
            // Measured on the pre-fix PR: h2+tritium reached level 6 in ~55 s, h2+oxygen in ~150 s.
            // The bounds are deliberately loose; they exist to catch a heat-rate rescale that makes
            // leaving level 0 take minutes (a 1/95 heat limiter, for instance).
            var tritium = MeasureHeatUp(entMan, testMap.GridCoords, protoMan, hfr, "h2T2FuelRecipe", maxSeconds: 600);
            Assert.Multiple(() =>
            {
                Assert.That(tritium.LevelTimes[6], Is.GreaterThan(5.0),
                    "the h2+tritium reactor heats up implausibly fast");
                Assert.That(tritium.LevelTimes[6], Is.LessThan(240.0),
                    $"the h2+tritium reactor took {tritium.LevelTimes[6]:0}s to reach power level 6 (was ~55 s)");
                Assert.That(tritium.LevelTimes[1], Is.LessThan(60.0),
                    $"the h2+tritium reactor took {tritium.LevelTimes[1]:0}s to leave power level 0; the heat limiter is binding at ambient temperature");
                Assert.That(tritium.MaximumTemperature, Is.LessThanOrEqualTo(Atmospherics.Tmax));
            });

            var oxygen = MeasureHeatUp(entMan, testMap.GridCoords.Offset(new System.Numerics.Vector2(10f, 0f)), protoMan, hfr, "h2O2FuelRecipe", maxSeconds: 1200);
            Assert.Multiple(() =>
            {
                Assert.That(oxygen.LevelTimes[6], Is.GreaterThan(5.0),
                    "the h2+oxygen reactor heats up implausibly fast");
                Assert.That(oxygen.LevelTimes[6], Is.LessThan(600.0),
                    $"the h2+oxygen reactor took {oxygen.LevelTimes[6]:0}s to reach power level 6 (was ~150 s)");
            });
        });

        await pair.CleanReturnAsync();
    }

    private static (Dictionary<int, double> LevelTimes, float MaximumTemperature) MeasureHeatUp(
        IEntityManager entMan,
        EntityCoordinates coords,
        IPrototypeManager protoMan,
        HypertorusFusionReactorSystem hfr,
        string recipeId,
        double maxSeconds)
    {
        var recipe = protoMan.Index<FusionRecipePrototype>(recipeId);
        var coreUid = entMan.SpawnEntity("HFRCore", coords);
        var core = entMan.GetComponent<HFRCoreComponent>(coreUid);
        HFRTemperatureScaleTest.FillCoreForRecipe(core, recipe);

        var levelTimes = new Dictionary<int, double>();
        var lastLevel = -1;
        var simSeconds = 0.0;
        var maximumTemperature = core.InternalFusion?.Temperature ?? Atmospherics.T20C;
        var maxTicks = (int) (maxSeconds / DeviceDt) + 1;

        for (var tick = 0; tick < maxTicks; tick++)
        {
            // Approximate a continuous fuel feed. The core has no console in this harness, so
            // FusionProcess uses its unpowered fallback of 500 K/s for the heating conductor - the same
            // setting the guide tells players to use to kick the reaction off. A powered console
            // starts at the component default (50) and heats proportionally slower until the player
            // raises it.
            foreach (var gas in recipe.Requirements)
                core.InternalFusion!.SetMoles(Enum.Parse<Gas>(gas), 1000f);

            hfr.FusionProcess(coreUid, core, DeviceDt);
            simSeconds += DeviceDt;

            if (core.PowerLevel != lastLevel)
            {
                levelTimes[core.PowerLevel] = simSeconds;
                lastLevel = core.PowerLevel;
            }

            maximumTemperature = Math.Max(maximumTemperature, core.InternalFusion?.Temperature ?? 0f);

            if (core.PowerLevel == 6 &&
                core.FusionTemperature >= HypertorusFusionReactor.FusionMaximumTemperature * recipe.TemperatureChangeMultiplier * 0.99f)
            {
                break;
            }
        }

        TestContext.Progress.WriteLine(
            $"[HFR heat-up] {recipeId}: " +
            string.Join(", ", levelTimes.Select(entry => $"L{entry.Key}={entry.Value:0.0}s")) +
            $", max={maximumTemperature:0} K");

        entMan.DeleteEntity(coreUid);
        return (levelTimes, maximumTemperature);
    }
}
