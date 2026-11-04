using System.Numerics;
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: test helpers for the bluespace power transmitter's grid wiring.
/// </summary>
public static class ShipCombatTestHelpers
{
    /// <summary>
    /// Spawns a transmitter at <paramref name="sinkTile"/> with an HV cable under it, fed by a
    /// generator on the tile north of it. The generator is boosted so tests do not have to wait for
    /// the supply ramp.
    /// </summary>
    public static EntityUid SpawnPoweredTransmitter(
        IEntityManager entMan,
        SharedMapSystem mapSystem,
        EntityUid grid,
        Vector2 sinkTile)
    {
        var gridComp = entMan.GetComponent<MapGridComponent>(grid);
        var generatorTile = new Vector2i((int) sinkTile.X, (int) sinkTile.Y + 1);
        mapSystem.SetTile(grid, gridComp, generatorTile, new Tile(1));

        var generatorCoords = new EntityCoordinates(grid, new Vector2(generatorTile.X + 0.5f, generatorTile.Y + 0.5f));
        var generator = entMan.SpawnEntity("GeneratorRTG", generatorCoords);
        entMan.SpawnEntity("CableHV", generatorCoords);

        // The transmitter is a normal machine: the HV cable runs underneath it.
        entMan.SpawnEntity("CableHV", new EntityCoordinates(grid, sinkTile));

        var supplier = entMan.GetComponent<PowerSupplierComponent>(generator);
        supplier.MaxSupply = 100_000f;
        supplier.SupplyRampTolerance = 100_000f;
        supplier.SupplyRampRate = 100_000f;

        var xform = entMan.System<SharedTransformSystem>();
        var sink = entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, sinkTile));
        if (!entMan.GetComponent<TransformComponent>(sink).Anchored)
            xform.AnchorEntity(sink);

        return sink;
    }
}
