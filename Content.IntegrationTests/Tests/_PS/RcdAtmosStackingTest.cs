using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the RPD refuses to stack two plain pipe segments on one layer (their sprites share a
/// draw depth and z-fight) and refuses conflicting pipe nodes, while still allowing the legitimate
/// overlaps atmos construction depends on (a vent or pump on the pipe that feeds it, and the same
/// pipe on a different atmos layer).
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
        var mapManager = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
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
        var mapManager = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var rcdSystem = entManager.System<RCDSystem>();
        var anchorable = entManager.System<AnchorableSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
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
    public async Task RpdBlocksAnyPipeSegmentOnSameLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var plating = new Tile(tileDefMan["Plating"].TileId);
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 0), plating);

            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", coords);
            entManager.SpawnEntity("GasPipeStraight", coords);

            var rpd = entManager.SpawnEntity("RPD", coords);
            var rcd = entManager.GetComponent<RCDComponent>(rpd);
            SetMode(entManager, rpd, "PipeStraight");
            var tile = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(0, 0));

            Assert.Multiple(() =>
            {
                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, tile, new Vector2i(0, 0), null, user, false, direction: Direction.East),
                    Is.False,
                    "Even a non-conflicting pipe segment must not share a layer; the two sprites z-fight.");

                Assert.That(
                    rcdSystem.IsRCDOperationStillValid(rpd, rcd, grid.Owner, grid.Comp, tile, new Vector2i(0, 0), null, user, false, direction: Direction.South),
                    Is.False,
                    "A second north-south pipe on the same layer conflicts with the existing one.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientPrototypesCarryPipeSegmentTag()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var segmentTag = new ProtoId<TagPrototype>("AtmosPipeSegment");

        await client.WaitAssertion(() =>
        {
            var protoManager = client.ResolveDependency<IPrototypeManager>();
            var componentFactory = client.ResolveDependency<IComponentFactory>();

            foreach (var id in new[]
                     {
                         "GasPipeHalf", "GasPipeStraight", "GasPipeBend", "GasPipeTJunction",
                         "GasPipeFourway", "GasPipeManifold", "GasPipeBroken", "GasPipeStraightAlt1",
                     })
            {
                var proto = protoManager.Index<EntityPrototype>(id);

                Assert.That(
                    proto.TryGetComponent<TagComponent>(out var tagComp, componentFactory),
                    Is.True,
                    $"{id} has no TagComponent on the client.");

                // TagComponent.Tags is access-restricted to TagSystem, and the test client does not
                // register TagSystem, so read the field directly for this prototype check.
                var tagsField = typeof(TagComponent).GetField("Tags", BindingFlags.Public | BindingFlags.Instance);
                var protoTags = (HashSet<ProtoId<TagPrototype>>) tagsField!.GetValue(tagComp)!;

                Assert.That(
                    protoTags.Contains(segmentTag),
                    Is.True,
                    $"{id} is missing AtmosPipeSegment on the client.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
