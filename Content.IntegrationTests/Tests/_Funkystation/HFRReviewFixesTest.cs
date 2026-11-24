using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Numerics;
using Content.Server._Funkystation.Atmos.Components;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server._Funkystation.Atmos.Systems;
using Content.Server._Funkystation.Atmos.HFR.Systems;
using Content.Server.Construction.Conditions;
using Content.Server.Power.Components;
using Content.Server.Singularity.Components;
using Content.Shared._Funkystation.Atmos.Components;
using Content.Shared._Funkystation.Atmos.HFR;
using Content.Shared._Funkystation.Atmos.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.Prototypes;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Spawners;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Regression coverage for the HFR review: the fusion math must not persist non-finite values,
///     a power level 6 gravity pulse has to use its intended range without permanently changing the
///     gravity well, and active parts must stay blocked from welding.
/// </summary>
[TestFixture]
public sealed class HFRReviewFixesTest
{
    // Uses the pooled pair on purpose: the integration suite has a 20 minute cap and a fresh
    // (destructive) pair costs a full server+client boot. These tests only spawn and delete
    // entities on the pair's own test map.
    [Test]
    public async Task FusionProcessKeepsReactorValuesFinite()
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
        await using var pair = await PoolManager.GetServerClient();
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
        await using var pair = await PoolManager.GetServerClient();
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
    ///     A part that has been unbolted or deleted must block activation: otherwise the reactor
    ///     starts without a waste/fuel path and cannot be shut down again once its power level rises.
    /// </summary>
    [Test]
    public async Task ActivationIsRefusedWhenTheAssemblyIsIncomplete()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();

        EntityUid core = default;
        EntityUid console = default;
        EntityUid waste = default;

        await server.WaitAssertion(() =>
        {
            (core, console, _, _, waste, _) = SpawnAssembly(server, testMap.Grid);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var coreComp = entMan.GetComponent<HFRCoreComponent>(core);
            Assert.That(hfr.AreAllPartsConnected(core, coreComp), Is.True, "the test reactor did not assemble");

            // Unbolting a part must break the assembly.
            server.System<SharedTransformSystem>().Unanchor(waste, entMan.GetComponent<TransformComponent>(waste));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var coreComp = entMan.GetComponent<HFRCoreComponent>(core);
            Assert.That(hfr.AreAllPartsConnected(core, coreComp), Is.False, "an unbolted part still counts as connected");

            entMan.EventBus.RaiseLocalEvent(console, new HFRConsoleTogglePowerMessage());
            Assert.That(coreComp.IsActive, Is.False, "the reactor was activated with an unbolted part");
        });

        // Re-anchoring the part restores the assembly, and only then may the reactor start.
        await server.WaitAssertion(() =>
        {
            server.System<SharedTransformSystem>().AnchorEntity(waste, entMan.GetComponent<TransformComponent>(waste));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var coreComp = entMan.GetComponent<HFRCoreComponent>(core);
            Assert.That(hfr.AreAllPartsConnected(core, coreComp), Is.True, "the re-anchored part was not re-linked");

            entMan.EventBus.RaiseLocalEvent(console, new HFRConsoleTogglePowerMessage());
            Assert.That(coreComp.IsActive, Is.True, "the assembled reactor did not start");

            // Losing a part while running deactivates the reactor on the next fusion tick, and the
            // reaction still behaves like a normal shutdown (power level keeps it melting down).
            entMan.DeleteEntity(waste);
            hfr.Process(core, coreComp, 10f / 15f);
            Assert.That(coreComp.IsActive, Is.False, "the reactor kept running after losing a part");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The meltdown flags are read from the selected recipe at detonation, so the recipe must not
    ///     be swappable while the reactor is running or counting down.
    /// </summary>
    [Test]
    public async Task RecipeCannotBeSwappedWhileRunning()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();

        EntityUid core = default;
        EntityUid console = default;

        await server.WaitAssertion(() =>
        {
            (core, console, _, _, _, _) = SpawnAssembly(server, testMap.Grid);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var coreComp = entMan.GetComponent<HFRCoreComponent>(core);
            var firstRecipe = protoMan.EnumeratePrototypes<FusionRecipePrototype>().First();
            var secondRecipe = protoMan.EnumeratePrototypes<FusionRecipePrototype>().Last();
            Assert.That(firstRecipe.ID, Is.Not.EqualTo(secondRecipe.ID));

            // Idle reactor: the change is allowed, and leftover fusion gases are dumped.
            coreComp.SelectedRecipeId = firstRecipe.ID;
            coreComp.InternalFusion = new GasMixture(HypertorusFusionReactor.InternalFusionVolume) { Temperature = Atmospherics.T20C };
            coreComp.InternalFusion.SetMoles(Gas.Hydrogen, 100f);
            entMan.EventBus.RaiseLocalEvent(console, new HFRConsoleSelectRecipeMessage(secondRecipe.ID));
            Assert.That(coreComp.SelectedRecipeId, Is.EqualTo(secondRecipe.ID), "an idle reactor refused a recipe change");
            Assert.That(coreComp.InternalFusion.TotalMoles, Is.Zero, "the old fusion gases were not dumped on a recipe change");

            // Running reactor (active or merely still hot): the change must be rejected.
            coreComp.SelectedRecipeId = firstRecipe.ID;
            coreComp.IsActive = true;
            entMan.EventBus.RaiseLocalEvent(console, new HFRConsoleSelectRecipeMessage(secondRecipe.ID));
            Assert.That(coreComp.SelectedRecipeId, Is.EqualTo(firstRecipe.ID), "the recipe was swapped while the reactor was active");

            coreComp.IsActive = false;
            coreComp.PowerLevel = 1;
            entMan.EventBus.RaiseLocalEvent(console, new HFRConsoleSelectRecipeMessage(secondRecipe.ID));
            Assert.That(coreComp.SelectedRecipeId, Is.EqualTo(firstRecipe.ID), "the recipe was swapped while the reactor was hot");

            coreComp.PowerLevel = 0;
            hfr.ToggleActiveState(core, coreComp, false);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The console must drop its 100 kW running draw when the core melts down and despawns. The
    ///     old code kept it at 100 kW forever because the load was only written on an active toggle.
    /// </summary>
    [Test]
    public async Task ConsoleDropsToIdleLoadAfterMeltdown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();

        EntityUid core = default;
        EntityUid console = default;

        await server.WaitAssertion(() =>
        {
            (core, console, _, _, _, _) = SpawnAssembly(server, testMap.Grid);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var coreComp = entMan.GetComponent<HFRCoreComponent>(core);
            var consolePower = entMan.GetComponent<ApcPowerReceiverComponent>(console);

            hfr.ToggleActiveState(core, coreComp, true);
            Assert.Multiple(() =>
            {
                Assert.That(coreComp.IsActive, Is.True, "the reactor did not start");
                Assert.That(consolePower.Load, Is.EqualTo(HFRConsoleSystem.RunningPowerLoad),
                    "a running reactor's console does not draw the running load");
            });

            // Fill the reactor up so the meltdown emits a meaningful amount of gas.
            coreComp.PowerLevel = 6;
            var recipe = protoMan.EnumeratePrototypes<FusionRecipePrototype>().First(candidate => candidate.Requirements.Count > 0);
            HFRTemperatureScaleTest.FillCoreForRecipe(coreComp, recipe);

            hfr.Meltdown(core, coreComp);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<TimedDespawnComponent>(core), Is.True,
                    "the core was not scheduled to despawn after the meltdown");
                Assert.That(consolePower.Load, Is.EqualTo(HFRConsoleSystem.IdlePowerLoad),
                    "the console kept the running load after the meltdown");
                Assert.That(coreComp.InternalFusion?.TotalMoles ?? 0f, Is.Zero,
                    "the meltdown leaked fusion gas instead of emitting all of it");
                Assert.That(coreComp.ModeratorInternal?.TotalMoles ?? 0f, Is.Zero,
                    "the meltdown leaked moderator gas instead of emitting all of it");
            });
        });

        // Wait for the core to despawn and make sure the surviving console stays on idle power.
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(core), Is.False, "the melted down core did not despawn");
            var consolePower = entMan.GetComponent<ApcPowerReceiverComponent>(console);
            var consoleComp = entMan.GetComponent<HFRConsoleComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(consolePower.Load, Is.EqualTo(HFRConsoleSystem.IdlePowerLoad),
                    "the console is still drawing the running load after the core despawned");
                Assert.That(consoleComp.CoreUid, Is.Null, "the console kept a link to the despawned core");
            });
        });

        await pair.CleanReturnAsync();
    }


    /// <summary>
    ///     Every mole in the fusion and moderator mixtures has to be emitted when the core melts down.
    ///     The old meltdown dropped ~12% of the fusion mix in the leftover of each RemoveRatio batch,
    ///     ~63% of the 20% pocket batch and ~35% of the final 3x3 dump, so this checks the tile air
    ///     instead of only checking that the component fields were nulled.
    /// </summary>
    [Test]
    public async Task MeltdownEmitsEveryMoleIntoTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();
        var atmos = entMan.System<AtmosphereSystem>();
        var mapManager = server.ResolveDependency<IMapManager>();

        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            (core, _, _, _, _, _) = SpawnAssembly(server, testMap.Grid);

            // A flat test grid only gets its GridAtmosphereComponent from AutomaticAtmosSystem once
            // it is heavy enough, and the tiles set by SpawnAssembly must be processed into
            // TileAtmosphere entries before meltdown can emit into them. Force that now.
            var gridAtmos = entMan.GetComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
            atmos.InvalidateAllTiles((testMap.Grid.Owner, testMap.Grid.Comp, gridAtmos));
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var coreComp = entMan.GetComponent<HFRCoreComponent>(core);
            coreComp.PowerLevel = 6;

            var recipe = protoMan.EnumeratePrototypes<FusionRecipePrototype>()
                .First(candidate => candidate.Requirements.Count > 0);
            HFRTemperatureScaleTest.FillCoreForRecipe(coreComp, recipe);

            // Deliberately introduce clearly identifiable extra gas in both mixtures. Meltdown does
            // not consume anything, so every mole is expected in the tile air afterwards.
            coreComp.InternalFusion!.SetMoles(Gas.Hydrogen, 400f);
            coreComp.ModeratorInternal!.SetMoles(Gas.Nitrogen, 300f);
            var expected = coreComp.InternalFusion.TotalMoles + coreComp.ModeratorInternal.TotalMoles;
            Assert.That(expected, Is.GreaterThan(0f), "the test core has no gas to emit");

            // The meltdown queues an explosion for the next tick; the gas is emitted synchronously,
            // so summing right now measures emission before any explosion processing touches tiles.
            hfr.Meltdown(core, coreComp);

            var mapId = entMan.GetComponent<TransformComponent>(core).MapID;
            var mapUid = mapManager.GetMapEntityId(mapId);
            var gridEntity = new Entity<GridAtmosphereComponent?, GasTileOverlayComponent?>(
                testMap.Grid.Owner,
                entMan.GetComponent<GridAtmosphereComponent>(testMap.Grid.Owner),
                entMan.GetComponent<GasTileOverlayComponent>(testMap.Grid.Owner));
            var mapEntity = new Entity<MapAtmosphereComponent?>(
                mapUid,
                entMan.TryGetComponent<MapAtmosphereComponent>(mapUid, out var mapAtmos) ? mapAtmos : null);

            var emitted = 0f;
            var emittedAtoms = 0f;
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var mixture = atmos.GetTileMixture(gridEntity, mapEntity, new Vector2i(x, y));
                    if (mixture == null)
                        continue;

                    emitted += mixture.TotalMoles;
                    emittedAtoms += mixture.GetMoles(Gas.Hydrogen) + mixture.GetMoles(Gas.Nitrogen);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(emitted, Is.GreaterThan(0f), "the meltdown emitted no gas at all");
                Assert.That(emitted, Is.EqualTo(expected).Within(Math.Max(expected * 0.005f, 0.1f)),
                    $"the meltdown emitted {emitted} of {expected} moles");
                // The extra marker gases must be recognisable in the emitted air too.
                Assert.That(emittedAtoms, Is.GreaterThanOrEqualTo(690f),
                    "the extra fusion/moderator gas was not emitted into the tiles");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The HFR part links are runtime state that is rebuilt from the anchored parts. A saved and
    ///     reloaded reactor must come back assembled (and keep its running flag), otherwise a cargo
    ///     shuttle or ship save would leave the reactor dead after a restart.
    /// </summary>
    [Test]
    public async Task AssemblyRelinksAfterSaveLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var resManager = server.ResolveDependency<IResourceManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var hfr = entMan.System<HypertorusFusionReactorSystem>();
        var savePath = new ResPath("/Maps/Test/HFRAssemblyReload.yml");

        await server.WaitAssertion(() =>
        {
            resManager.UserData.CreateDir(savePath.Directory);
            var (core, _, _, _, _, _) = SpawnAssembly(server, testMap.Grid);
            entMan.GetComponent<HFRCoreComponent>(core).IsActive = true;
            Assert.That(mapLoader.TrySaveMap(testMap.MapId, savePath), Is.True);
            mapSystem.DeleteMap(testMap.MapId);
        });

        await server.WaitIdleAsync();

        MapId loadedMap = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(savePath, out var loaded, out _), Is.True);
            loadedMap = loaded!.Value.Comp.MapId;
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var cores = new List<Entity<HFRCoreComponent>>();
            var query = entMan.EntityQueryEnumerator<HFRCoreComponent>();
            while (query.MoveNext(out var uid, out var comp))
                cores.Add((uid, comp));

            Assert.That(cores, Has.Count.EqualTo(1), "expected exactly one reactor after the load");
            var (core, coreComp) = cores[0];

            Assert.Multiple(() =>
            {
                Assert.That(coreComp.IsActive, Is.True, "the running state did not survive the save/load");
                Assert.That(hfr.AreAllPartsConnected(core, coreComp), Is.True,
                    "the reactor parts did not relink after the save/load");
            });

            // Every part should have its reverse link rebuilt too.
            foreach (var cornerUid in coreComp.CornerUids)
            {
                Assert.That(entMan.GetComponent<HFRCornerComponent>(cornerUid).CoreUid, Is.EqualTo(core),
                    "a corner did not relink to the core");
            }

            if (coreComp.ConsoleUid is { } consoleUid)
            {
                Assert.That(entMan.GetComponent<HFRConsoleComponent>(consoleUid).CoreUid, Is.EqualTo(core),
                    "the console did not relink to the core");
            }

            mapSystem.DeleteMap(loadedMap);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Spawns the full 3x3 HFR multiblock on a test grid.
    /// </summary>
    private static (EntityUid Core, EntityUid Console, EntityUid Fuel, EntityUid Moderator, EntityUid Waste, EntityUid[] Corners)
        SpawnAssembly(RobustIntegrationTest.ServerIntegrationInstance server, Entity<MapGridComponent> grid)
    {
        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        var plating = tiles["Plating"];
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
                mapSys.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
        }

        var center = new EntityCoordinates(grid.Owner, new System.Numerics.Vector2(0.5f, 0.5f));
        var core = entMan.SpawnEntity("HFRCore", center);
        var console = entMan.SpawnEntity("HFRConsole", center.Offset(new System.Numerics.Vector2(0f, 1f)));
        var fuel = entMan.SpawnEntity("HFRFuelInput", center.Offset(new System.Numerics.Vector2(1f, 0f)));
        var moderator = entMan.SpawnEntity("HFRModeratorInput", center.Offset(new System.Numerics.Vector2(-1f, 0f)));
        var waste = entMan.SpawnEntity("HFRWasteOutput", center.Offset(new System.Numerics.Vector2(0f, -1f)));
        var corners = new[]
        {
            entMan.SpawnEntity("HFRCorner", center.Offset(new System.Numerics.Vector2(1f, 1f))),
            entMan.SpawnEntity("HFRCorner", center.Offset(new System.Numerics.Vector2(-1f, 1f))),
            entMan.SpawnEntity("HFRCorner", center.Offset(new System.Numerics.Vector2(1f, -1f))),
            entMan.SpawnEntity("HFRCorner", center.Offset(new System.Numerics.Vector2(-1f, -1f))),
        };

        return (core, console, fuel, moderator, waste, corners);
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
