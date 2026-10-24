using System.Numerics;
using Content.Shared.Atmos.Components;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the RPD refuses to stack a pipe/device on a tile that already has one on the same
/// atmos layer. Pipes carry no fixtures, so the RCD collision check cannot see them; the guard
/// uses the `Unstackable` tag instead.
/// </summary>
[TestFixture]
public sealed class RcdAtmosStackingTest
{
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
}
