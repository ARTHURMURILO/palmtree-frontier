using System;
using System.Linq;
using System.Reflection;
using System.Numerics;
using Content.Server._Funkystation.Atmos.Components;
using Content.Server._Funkystation.Atmos.HFR.Systems;
using Content.Server.Construction.Conditions;
using Content.Server.Singularity.Components;
using Content.Shared._Funkystation.Atmos.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Construction.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Regression coverage for the HFR review: the fusion math must not persist non-finite values,
///     a power level 6 gravity pulse has to use its intended range without permanently changing the
///     gravity well, and active parts must stay blocked from welding.
/// </summary>
[TestFixture]
public sealed class HFRReviewFixesTest
{
    [Test]
    public async Task FusionProcessKeepsReactorValuesFinite()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();

        await server.WaitAssertion(() =>
        {
            var coreUid = entMan.SpawnEntity("HFRCore", testMap.GridCoords);
            var core = entMan.GetComponent<HFRCoreComponent>(coreUid);

            var recipe = protoMan.EnumeratePrototypes<FusionRecipePrototype>()
                .First(candidate => candidate.Requirements.Count > 0);
            core.SelectedRecipeId = recipe.ID;

            core.InternalFusion = new GasMixture(5000f) { Temperature = 1000f };
            foreach (var gas in recipe.Requirements)
                core.InternalFusion.SetMoles(Enum.Parse<Gas>(gas), 1000f);

            // A non-finite gas value used to make the gas power overflow: Inf % N is NaN, and from
            // there NaN reached the stored instability.
            SetRawMoles(core.InternalFusion, Gas.Oxygen, float.NaN);

            // Poison the moderator too, so the energy modifiers (and therefore the heat output)
            // become non-finite as well.
            core.ModeratorInternal = new GasMixture(10000f) { Temperature = 300f };
            core.ModeratorInternal.SetMoles(Gas.Nitrogen, 100f);
            SetRawMoles(core.ModeratorInternal, Gas.Nitrogen, float.NaN);

            hfr.FusionProcess(coreUid, core, 0.5f);

            Assert.Multiple(() =>
            {
                Assert.That(float.IsFinite(core.Instability), Is.True,
                    $"instability became {core.Instability}");
                Assert.That(float.IsFinite(core.HeatOutput), Is.True,
                    $"heat output became {core.HeatOutput}");
                Assert.That(core.InternalFusion, Is.Not.Null);
                Assert.That(float.IsFinite(core.InternalFusion!.Temperature), Is.True,
                    "the fusion mix temperature became non-finite");
            });

            entMan.DeleteEntity(coreUid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GravityPulseUsesLevelSixRangeAndRestoresWellRange()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid coreUid = default;
        EntityUid item = default;
        await server.WaitAssertion(() =>
        {
            coreUid = entMan.SpawnEntity("HFRCore", testMap.GridCoords);
            item = entMan.SpawnEntity("Crowbar", testMap.GridCoords.Offset(new Vector2(3.5f, 0f)));
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var core = entMan.GetComponent<HFRCoreComponent>(coreUid);

            Assert.That(entMan.TryGetComponent<GravityWellComponent>(coreUid, out var well), Is.True,
                "the HFR core has no gravity well");
            var configuredRange = well!.MaxRange;

            core.PowerLevel = 6;
            core.NextGravityPulse = TimeSpan.Zero;

            var hfr = entMan.System<HypertorusFusionReactorSystem>();
            // First call only sets the next pulse time; the second one is due immediately.
            hfr.CheckGravityPulse(coreUid, core, 0.5f);
            core.NextGravityPulse = TimeSpan.FromTicks(1);
            hfr.CheckGravityPulse(coreUid, core, 0.5f);

            Assert.Multiple(() =>
            {
                Assert.That(well.MaxRange, Is.EqualTo(configuredRange),
                    "the gravity pulse overwrote the well's configured max range");

                var physics = entMan.GetComponent<PhysicsComponent>(item);
                Assert.That(physics.LinearVelocity.Length(), Is.GreaterThan(0.01f),
                    "an entity 3.5 tiles away was not pushed; level 6 is still clamped to the level 5 range");
            });

            entMan.DeleteEntity(coreUid);
            entMan.DeleteEntity(item);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HfrPartsBlockWeldingWhileActive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        // Every HFR part has a deconstruction edge guarded by the HFRIsActive condition.
        var graphIds = new[]
        {
            "HFRCore", "HFRCorner", "HFRConsole", "HFRFuelInput", "HFRModeratorInput", "HFRWasteOutput",
        };

        await server.WaitAssertion(() =>
        {
            HFRIsActive? coreGuard = null;

            foreach (var graphId in graphIds)
            {
                var graph = protoMan.Index<ConstructionGraphPrototype>(graphId);
                HFRIsActive? guard = null;

                foreach (var node in graph.Nodes.Values)
                {
                    foreach (var edge in node.Edges)
                    {
                        foreach (var condition in edge.Conditions)
                        {
                            if (condition is HFRIsActive active)
                                guard = active;
                        }
                    }
                }

                Assert.That(guard, Is.Not.Null, $"{graphId} lost its active-weld guard");

                if (graphId == "HFRCore")
                    coreGuard = guard;
            }

            var coreUid = entMan.SpawnEntity("HFRCore", testMap.GridCoords);
            var core = entMan.GetComponent<HFRCoreComponent>(coreUid);

            Assert.That(coreGuard!.Condition(coreUid, entMan), Is.True,
                "an idle HFR core should be weldable");
            core.IsActive = true;
            Assert.That(coreGuard.Condition(coreUid, entMan), Is.False,
                "an active HFR core must not be weldable");
            core.IsActive = false;
            core.PowerLevel = 1;
            Assert.That(coreGuard.Condition(coreUid, entMan), Is.False,
                "an HFR core that still has power must not be weldable");
            core.PowerLevel = 0;
            Assert.That(coreGuard.Condition(coreUid, entMan), Is.True,
                "an idle HFR core should be weldable again");

            entMan.DeleteEntity(coreUid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The public setter refuses non-finite amounts, which is exactly the invariant the
    ///     consumers of the mixture rely on. Write through the backing array so the failure mode
    ///     can be reproduced at all.
    /// </summary>
    private static void SetRawMoles(GasMixture mixture, Gas gas, float value)
    {
        var field = typeof(GasMixture).GetField("Moles",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var moles = (float[]) field!.GetValue(mixture)!;
        moles[(int) gas] = value;
    }
}
