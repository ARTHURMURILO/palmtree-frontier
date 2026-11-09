using System.Linq;
using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Construction;

/// <summary>
/// Palmtree: directional windows only cover one edge of a tile. They should not stop furniture,
/// machinery or pipes from sharing the tile, but they should still keep full-tile builds out.
/// </summary>
[TestFixture]
public sealed class DirectionalWindowPlacementTest
{
    [Test]
    public async Task NonFullTileBuildsShareDirectionalWindowTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var anchorable = entMan.System<AnchorableSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapMan.CreateGridEntity(mapId);
            var plating = new Tile(tileDefMan["Plating"].TileId);
            mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, plating);
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 0), plating);

            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entMan.SpawnEntity("MobHuman", coords);
            var window = entMan.SpawnEntity("WindowDirectional", coords);
            Assert.That(entMan.GetComponent<TransformComponent>(window).Anchored, Is.True,
                "Directional windows are anchored by their BaseStructure prototype.");

            Assert.Multiple(() =>
            {
                Assert.That(CanBuild(protoMan, "Table", user, coords, Direction.South), Is.True,
                    "A table should be buildable on a directional window's tile.");
                Assert.That(CanBuild(protoMan, "Chair", user, coords, Direction.South), Is.True,
                    "Furniture should be buildable on a directional window's tile.");
                Assert.That(CanBuild(protoMan, "Wall", user, coords, Direction.South), Is.False,
                    "A wall still needs the whole tile.");
                Assert.That(CanBuild(protoMan, "Airlock", user, coords, Direction.South), Is.False,
                    "An airlock still needs the whole tile.");
                Assert.That(CanBuild(protoMan, "Window", user, coords, Direction.South), Is.False,
                    "A full window still cannot share a tile with a directional window.");
                Assert.That(CanBuild(protoMan, "Windoor", user, coords, Direction.South), Is.False,
                    "A windoor still needs the whole tile.");
                Assert.That(CanBuild(protoMan, "BarricadeDirectional", user, coords, Direction.South), Is.False,
                    "A directional barricade still needs the whole tile.");
            });

            // Anchoring/deploying a machine next to the window is allowed too. The machines are
            // spawned on the neighbouring tile so that they do not count as obstacles themselves.
            var neighbor = new EntityCoordinates(grid.Owner, new Vector2(1.5f, 0.5f));

            var machine = entMan.SpawnEntity("MachineFrame", neighbor);
            var machineBody = entMan.GetComponent<PhysicsComponent>(machine);
            Assert.That(anchorable.TileFree(coords, machineBody), Is.True,
                "A machine frame should be anchorable next to a directional window.");

            // Full-tile and full-height anchorables still need the tile to themselves.
            var assembly = entMan.SpawnEntity("AirlockAssembly", neighbor);
            var assemblyBody = entMan.GetComponent<PhysicsComponent>(assembly);
            Assert.That(anchorable.TileFree(coords, assemblyBody), Is.False,
                "A full-tile airlock assembly should not anchor over a directional window.");

            var fullWindow = entMan.SpawnEntity("Window", neighbor);
            var fullWindowBody = entMan.GetComponent<PhysicsComponent>(fullWindow);
            Assert.That(anchorable.TileFree(coords, fullWindowBody), Is.False,
                "A full window should not anchor over a directional window.");

            var airlock = entMan.SpawnEntity("Airlock", neighbor);
            var airlockBody = entMan.GetComponent<PhysicsComponent>(airlock);
            Assert.That(anchorable.TileFree(coords, airlockBody), Is.False,
                "An airlock should not anchor over a directional window.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DirectionalWindowPivotAllowsPerpendicularWindowOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entMan.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapMan.CreateGridEntity(mapId);
            var plating = new Tile(tileDefMan["Plating"].TileId);
            mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, plating);

            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entMan.SpawnEntity("MobHuman", coords);
            var window = entMan.SpawnEntity("WindowDirectional", coords);
            var windowXform = entMan.GetComponent<TransformComponent>(window);
            windowXform.LocalRotation = Direction.East.ToAngle();
            Assert.That(windowXform.LocalRotation.GetCardinalDir(), Is.EqualTo(Direction.East));

            Assert.Multiple(() =>
            {
                Assert.That(CanBuild(protoMan, "WindowDirectional", user, coords, Direction.East), Is.False,
                    "A second directional window on the same edge should be rejected.");
                Assert.That(CanBuild(protoMan, "WindowDirectional", user, coords, Direction.South), Is.True,
                    "A perpendicular directional window should be allowed to form a pivot.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RcdIgnoresDirectionalWindowsForNonFullTileBuilds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var rcdSystem = entMan.System<RCDSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapMan.CreateGridEntity(mapId);
            var plating = new Tile(tileDefMan["Plating"].TileId);
            mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, plating);

            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entMan.SpawnEntity("MobHuman", coords);
            var window = entMan.SpawnEntity("WindowDirectional", coords);
            var tileRef = mapSystem.GetTileRef(grid.Owner, grid.Comp, Vector2i.Zero);

            var rpd = entMan.SpawnEntity("RPD", coords);
            var rpdComp = entMan.GetComponent<RCDComponent>(rpd);
            SetRcdMode(entMan, rpd, "PipeTJunction");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rpd, rpdComp, grid.Owner, grid.Comp, tileRef, Vector2i.Zero,
                    null, user, false, AtmosPipeLayer.Primary),
                Is.True,
                "The RPD should be able to place a pipe under a directional window.");

            var rcd = entMan.SpawnEntity("RCD", coords);
            var rcdComp = entMan.GetComponent<RCDComponent>(rcd);
            SetRcdMode(entMan, rcd, "WallSolid");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef, Vector2i.Zero,
                    null, user, false, AtmosPipeLayer.Primary),
                Is.False,
                "The RCD should not place a wall over a directional window.");

            SetRcdMode(entMan, rcd, "Window");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef, Vector2i.Zero,
                    null, user, false, AtmosPipeLayer.Primary),
                Is.False,
                "The RCD should not place a full window over a directional window.");

            SetRcdMode(entMan, rcd, "Airlock");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef, Vector2i.Zero,
                    null, user, false, AtmosPipeLayer.Primary),
                Is.False,
                "The RCD should not place an airlock over a directional window.");

            SetRcdMode(entMan, rcd, "Firelock");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef, Vector2i.Zero,
                    null, user, false, AtmosPipeLayer.Primary),
                Is.False,
                "The RCD should not place a firelock over a directional window.");

            SetRcdMode(entMan, rcd, "WindowDirectional");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef, Vector2i.Zero,
                    null, user, false, AtmosPipeLayer.Primary),
                Is.False,
                "A second directional window on the same edge should be rejected by the RCD.");

            // The RCD builds towards its current ConstructionDirection (south by default). Turn the
            // existing window to the east so a new one would land on a different edge.
            entMan.GetComponent<TransformComponent>(window).LocalRotation = Direction.East.ToAngle();

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef, Vector2i.Zero,
                    null, user, false, AtmosPipeLayer.Primary),
                Is.True,
                "A perpendicular directional window should be allowed by the RCD.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TileUnderDirectionalWindowCanBePriedUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var hands = entMan.System<SharedHandsSystem>();

        var grid = default(Entity<MapGridComponent>);

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            grid = mapMan.CreateGridEntity(mapId);
            mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(tileDefMan["FloorGlass"].TileId));
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 1), new Tile(tileDefMan["Plating"].TileId));

            // The window's fixture is on the tile's south edge. The user approaches from the north,
            // so the window itself is not between them and the tile they are prying.
            var tileCoords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            entMan.SpawnEntity("WindowDirectional", tileCoords);

            var userCoords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 1.5f));
            var user = entMan.SpawnEntity("MobHuman", userCoords);
            var crowbar = entMan.SpawnEntity("Crowbar", userCoords);
            Assert.That(hands.TryPickupAnyHand(user, crowbar), Is.True,
                "Test setup: the user needs to hold the crowbar.");

            var ev = new AfterInteractEvent(user, crowbar, null, tileCoords, true);
            entMan.EventBus.RaiseLocalEvent(crowbar, ev);
            Assert.That(ev.Handled, Is.True,
                "A crowbar should be able to start prying a tile under a directional window.");
        });

        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var tile = mapSystem.GetTileRef(grid.Owner, grid.Comp, Vector2i.Zero);
            Assert.That(tile.Tile.TypeId, Is.EqualTo(tileDefMan["Plating"].TileId),
                "The glass floor under the directional window should have been pried up.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallStillBlocksPryingItsTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var hands = entMan.System<SharedHandsSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapMan.CreateGridEntity(mapId);
            mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(tileDefMan["FloorGlass"].TileId));
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 1), new Tile(tileDefMan["Plating"].TileId));

            var tileCoords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            entMan.SpawnEntity("WallSolid", tileCoords);

            var userCoords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 1.5f));
            var user = entMan.SpawnEntity("MobHuman", userCoords);
            var crowbar = entMan.SpawnEntity("Crowbar", userCoords);
            Assert.That(hands.TryPickupAnyHand(user, crowbar), Is.True,
                "Test setup: the user needs to hold the crowbar.");

            var ev = new AfterInteractEvent(user, crowbar, null, tileCoords, true);
            entMan.EventBus.RaiseLocalEvent(crowbar, ev);
            Assert.That(ev.Handled, Is.False,
                "A wall should still stop the player from prying the tile it stands on.");

            var tile = mapSystem.GetTileRef(grid.Owner, grid.Comp, Vector2i.Zero);
            Assert.That(tile.Tile.TypeId, Is.EqualTo(tileDefMan["FloorGlass"].TileId),
                "The glass floor under a wall should not be pried up.");
        });

        await pair.CleanReturnAsync();
    }

    private static void SetRcdMode(IEntityManager entMan, EntityUid rcd, string mode)
    {
        entMan.EventBus.RaiseLocalEvent(rcd, new RCDSystemMessage(mode));
    }

    private static bool CanBuild(IPrototypeManager protoMan, string id, EntityUid user, EntityCoordinates coords,
        Direction direction)
    {
        var prototype = protoMan.Index<ConstructionPrototype>(id);
        return prototype.Conditions.All(condition => condition.Condition(user, coords, direction));
    }
}
