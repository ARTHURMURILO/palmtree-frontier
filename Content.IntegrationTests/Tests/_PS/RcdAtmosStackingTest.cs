using System.Linq;
using System.Numerics;
using Content.Server.Construction.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the RPD follows the node-aware overlap rules. Perpendicular pipe segments may share
/// an atmos layer because their ports do not collide; parallel segments, conflicting pipe nodes
/// and same-layer unstackable devices are rejected. Legitimate overlaps atmos construction depends
/// on (a vent or pump on the pipe that feeds it, and the same pipe on a different atmos layer)
/// stay allowed.
/// </summary>
[TestFixture]
public sealed class RcdAtmosStackingTest
{
    private static void SetMode(IEntityManager entManager, EntityUid rcd, string mode)
    {
        // The RCD component's ProtoId is only writable by RCDSystem; go through its normal message
        // handler instead of writing the component from the test.
        entManager.EventBus.RaiseLocalEvent(rcd, new RCDSystemMessage(mode));
    }

    [Test]
    public async Task RpdCannotStackPipesOnSameLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = testMap.Grid;
            var plating = new Tile(tileDefMan["Plating"].TileId);

            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 0), plating);
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 0), plating);

            var occupied = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var empty = new EntityCoordinates(grid.Owner, new Vector2(1.5f, 0.5f));

            var user = entManager.SpawnEntity("MobHuman", occupied);
            entManager.SpawnEntity("GasPipeStraight", occupied);

            // The RPD's map init selects its first available mode (PipeFourway, a pipe).
            var rpd = entManager.SpawnEntity("RPD", occupied);
            var rcd = entManager.GetComponent<RCDComponent>(rpd);

            var occupiedTile = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(0, 0));
            var emptyTile = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(1, 0));

            Assert.Multiple(() =>
            {
                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, occupiedTile, new Vector2i(0, 0), null, user, false),
                    Is.False,
                    "RPD should refuse to stack a pipe on a tile that already has one on the primary layer.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, emptyTile, new Vector2i(1, 0), null, user, false),
                    Is.True,
                    "RPD should still be able to build pipes on an empty tile.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, occupiedTile, new Vector2i(0, 0), null, user, false, AtmosPipeLayer.Secondary),
                    Is.True,
                    "RPD should allow building on a different atmos layer.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RpdAllowsDevicesOnPipesButRejectsNodeConflicts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var rcdSystem = entManager.System<RCDSystem>();
        var anchorable = entManager.System<AnchorableSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = testMap.Grid;
            var plating = new Tile(tileDefMan["Plating"].TileId);

            for (var x = 0; x < 4; x++)
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), plating);

            // Sit in the middle so all four tiles are within interaction range.
            var userCoords = new EntityCoordinates(grid.Owner, new Vector2(2f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", userCoords);
            var rpd = entManager.SpawnEntity("RPD", userCoords);
            var rcd = entManager.GetComponent<RCDComponent>(rpd);

            // Tile 0: an east-west pipe; a south-facing vent does not conflict with it.
            var eastWest = entManager.SpawnEntity("GasPipeStraight", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            xformSystem.SetLocalRotation(eastWest, Angle.FromDegrees(90));

            // Tile 1: a north-south pipe; the vent's south node overlaps it.
            entManager.SpawnEntity("GasPipeStraight", new EntityCoordinates(grid.Owner, new Vector2(1.5f, 0.5f)));

            // Tile 2: a north-south pipe on the secondary layer; the vent goes on the primary layer.
            entManager.SpawnEntity("GasPipeStraightAlt1", new EntityCoordinates(grid.Owner, new Vector2(2.5f, 0.5f)));

            // Tile 3: an existing vent, which still blocks a second device on the same layer.
            entManager.SpawnEntity("GasVentPump", new EntityCoordinates(grid.Owner, new Vector2(3.5f, 0.5f)));

            SetMode(entManager, rpd, "VentGas");

            Assert.Multiple(() =>
            {
                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(0, 0)), new Vector2i(0, 0), null, user, false),
                    Is.True,
                    "A vent on a pipe with no overlapping node should be allowed.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(1, 0)), new Vector2i(1, 0), null, user, false),
                    Is.False,
                    "A vent whose node overlaps the pipe below it should be rejected.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(2, 0)), new Vector2i(2, 0), null, user, false),
                    Is.True,
                    "A vent on the primary layer may sit above a secondary-layer pipe.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(3, 0)), new Vector2i(3, 0), null, user, false),
                    Is.False,
                    "Two devices on the same layer are still rejected by the Unstackable rule.");

                Assert.That(
                    anchorable.AnyUnstackablesAnchoredAt(new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f))),
                    Is.False,
                    "A plain pipe must not make the construction menu report a stack of similar devices.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RpdAllowsPerpendicularPipeCrossingsOnSameLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = testMap.Grid;
            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", coords);

            // Existing north-south pipe on the primary layer.
            entManager.SpawnEntity("GasPipeStraight", coords);

            var rpd = entManager.SpawnEntity("RPD", coords);
            var rcd = entManager.GetComponent<RCDComponent>(rpd);
            SetMode(entManager, rpd, "PipeStraight");
            var tile = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(0, 0));

            Assert.Multiple(() =>
            {
                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, tile, new Vector2i(0, 0), null, user, false, direction: Direction.East),
                    Is.True,
                    "A perpendicular pipe crossing does not share a node direction, so it may share the layer.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, tile, new Vector2i(0, 0), null, user, false, direction: Direction.South),
                    Is.False,
                    "A second north-south pipe on the same layer overlaps the existing node and must be rejected.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnstackableAnchorCheckIgnoresOtherPipeLayers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var anchorable = entManager.System<AnchorableSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));
            entManager.SpawnEntity("GasVentPump", coords);

            Assert.Multiple(() =>
            {
                Assert.That(
                    anchorable.AnyUnstackablesAnchoredAt(coords, AtmosPipeLayer.Primary),
                    Is.True,
                    "A vent on the primary layer blocks another primary-layer unstackable.");

                Assert.That(
                    anchorable.AnyUnstackablesAnchoredAt(coords, AtmosPipeLayer.Secondary),
                    Is.False,
                    "A vent on the primary layer must not block a secondary-layer unstackable.");

                Assert.That(
                    anchorable.AnyUnstackablesAnchoredAt(coords),
                    Is.True,
                    "Callers that do not care about layers still see any unstackable.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnaryDevicesHaveLayerVariants()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protoManager = server.ResolveDependency<IPrototypeManager>();
            var factory = server.ResolveDependency<IComponentFactory>();

            Assert.Multiple(() =>
            {
                foreach (var (baseId, alt1Id, alt2Id) in new[]
                         {
                             ("GasVentPump", "GasVentPumpAlt1", "GasVentPumpAlt2"),
                             ("GasVentScrubber", "GasVentScrubberAlt1", "GasVentScrubberAlt2"),
                             ("GasPassiveVent", "GasPassiveVentAlt1", "GasPassiveVentAlt2"),
                             ("GasOutletInjector", "GasOutletInjectorAlt1", "GasOutletInjectorAlt2"),
                             ("GasDualPortVentPump", "GasDualPortVentPumpAlt1", "GasDualPortVentPumpAlt2"),
                         })
                {
                    var proto = protoManager.Index<EntityPrototype>(baseId);
                    Assert.That(
                        proto.TryGetComponent<AtmosPipeLayersComponent>(out var layers, factory),
                        Is.True,
                        $"{baseId} has no AtmosPipeLayers component.");

                    Assert.That(layers!.AlternativePrototypes, Does.ContainKey(AtmosPipeLayer.Secondary));
                    Assert.That(layers.AlternativePrototypes[AtmosPipeLayer.Secondary].Id, Is.EqualTo(alt1Id));
                    Assert.That(layers.AlternativePrototypes[AtmosPipeLayer.Tertiary].Id, Is.EqualTo(alt2Id));

                    var alt1 = protoManager.Index<EntityPrototype>(alt1Id);
                    Assert.That(
                        alt1.TryGetComponent<AtmosPipeLayersComponent>(out var alt1Layers, factory),
                        Is.True);
                    Assert.That(alt1Layers!.CurrentPipeLayer, Is.EqualTo(AtmosPipeLayer.Secondary));

                    var alt2 = protoManager.Index<EntityPrototype>(alt2Id);
                    Assert.That(
                        alt2.TryGetComponent<AtmosPipeLayersComponent>(out var alt2Layers, factory),
                        Is.True);
                    Assert.That(alt2Layers!.CurrentPipeLayer, Is.EqualTo(AtmosPipeLayer.Tertiary));
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnaryDevicesHaveLayeredConstructionRecipes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protoManager = server.ResolveDependency<IPrototypeManager>();

            foreach (var (baseId, alt1Id, alt2Id, node) in new[]
                     {
                         ("GasVentPump", "GasVentPumpAlt1", "GasVentPumpAlt2", "ventpump"),
                         ("GasVentScrubber", "GasVentScrubberAlt1", "GasVentScrubberAlt2", "ventscrubber"),
                         ("GasPassiveVent", "GasPassiveVentAlt1", "GasPassiveVentAlt2", "passivevent"),
                         ("GasOutletInjector", "GasOutletInjectorAlt1", "GasOutletInjectorAlt2", "outletinjector"),
                         ("GasDualPortVentPump", "GasDualPortVentPumpAlt1", "GasDualPortVentPumpAlt2", "dualportventpump"),
                     })
            {
                var recipe = protoManager.Index<ConstructionPrototype>(baseId);

                Assert.Multiple(() =>
                {
                    Assert.That(recipe.PlacementMode, Is.EqualTo("AlignAtmosPipeLayers"), $"{baseId} must pick a layer while placing.");
                    Assert.That(recipe.TargetNode, Is.EqualTo(node));
                    Assert.That(
                        recipe.AlternativePrototypes.Select(p => p.Id),
                        Is.EqualTo(new[] { baseId, alt1Id, alt2Id }),
                        $"{baseId} must offer the three pipe-layer recipes in enum order.");
                });

                var alt1 = protoManager.Index<ConstructionPrototype>(alt1Id);
                var alt2 = protoManager.Index<ConstructionPrototype>(alt2Id);
                Assert.Multiple(() =>
                {
                    Assert.That(alt1.Hide, Is.True);
                    Assert.That(alt2.Hide, Is.True);
                    Assert.That(alt1.TargetNode, Is.EqualTo($"{node}Alt1"));
                    Assert.That(alt2.TargetNode, Is.EqualTo($"{node}Alt2"));
                });
            }
        });

        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task DualPortVentAltEntitiesUseAltConstructionNodes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protoManager = server.ResolveDependency<IPrototypeManager>();
            var factory = server.ResolveDependency<IComponentFactory>();

            foreach (var (id, node) in new[]
                     {
                         ("GasDualPortVentPumpAlt1", "dualportventpumpAlt1"),
                         ("GasDualPortVentPumpAlt2", "dualportventpumpAlt2"),
                     })
            {
                var proto = protoManager.Index<EntityPrototype>(id);
                Assert.That(
                    proto.TryGetComponent<ConstructionComponent>(out var construction, factory),
                    Is.True,
                    $"{id} has no Construction component.");
                Assert.That(
                    construction!.Node,
                    Is.EqualTo(node),
                    $"{id} must deconstruct through its own alt graph node.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    [TestCase("GasPressurePump", "PressurePump")]
    [TestCase("GasValve", "ManualValve")]
    public async Task InlineBinaryDevicesMayShareATileWhenTheirPortsDoNotConflict(string deviceProto, string mode)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var rcdSystem = entManager.System<RCDSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = testMap.Grid;
            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", coords);

            // An east-west pump. A second north-south pump has no port in common with it, so the
            // node-aware check accepts the shared tile; this is the overlap inline devices need.
            var existing = entManager.SpawnEntity(deviceProto, coords);
            xformSystem.SetLocalRotation(existing, Angle.FromDegrees(90));

            var rpd = entManager.SpawnEntity("RPD", coords);
            var rcd = entManager.GetComponent<RCDComponent>(rpd);
            SetMode(entManager, rpd, mode);
            var tile = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(0, 0));

            Assert.Multiple(() =>
            {
                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, tile, new Vector2i(0, 0), null, user, false, direction: Direction.North),
                    Is.True,
                    "A perpendicular inline device has no conflicting port and may share the tile.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, tile, new Vector2i(0, 0), null, user, false, direction: Direction.East),
                    Is.False,
                    "A second inline device with the same ports must still be rejected.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
