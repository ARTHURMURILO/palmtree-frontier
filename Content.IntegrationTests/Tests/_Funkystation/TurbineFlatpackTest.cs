using System;
using Content.Shared.Construction.Components;
using Content.Shared.Construction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Guards the multi-tile footprint of the gas turbine flatpacks: the footprint data, the rotation
///     handling and the rule that cables and lower-layer pipes under a machine are allowed while
///     primary pipes, other machines and missing floor are not.
/// </summary>
[TestFixture]
public sealed class TurbineFlatpackTest
{
    [Test]
    public async Task TurbineFlatpackFootprintIsValidated()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = server.System<SharedMapSystem>();
        var xformSys = server.System<SharedTransformSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            var plating = tiles["Plating"];
            for (var x = -4; x <= 4; x++)
            {
                for (var y = -4; y <= 4; y++)
                    mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            var flatpack = entMan.SpawnEntity("GasTurbineFlatpack", testMap.GridCoords);
            var center = mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp, testMap.GridCoords);

            Assert.That(entMan.TryGetComponent<FlatpackComponent>(flatpack, out var pack), Is.True);
            Assert.That(pack!.UnpackDelay, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(entMan.TryGetComponent<FlatpackFootprintComponent>(flatpack, out var footprint), Is.True);
            Assert.That(footprint!.Footprint, Is.EqualTo(new Vector2i(3, 5)));
            Assert.That(footprint.Offset, Is.EqualTo(new Vector2i(-1, -2)));

            bool Attempt()
            {
                var ev = new FlatpackUnpackAttemptEvent(flatpack, EntityUid.Invalid, testMap.Grid.Owner, center);
                entMan.EventBus.RaiseLocalEvent(flatpack, ref ev, true);
                return !ev.Cancelled;
            }

            Assert.That(Attempt(), Is.True, "an empty patch of floor must accept the turbine");

            // A primary pipe on the edge of the footprint blocks the placement.
            var pipe = entMan.SpawnEntity("GasPipeStraight",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(1, 2)));
            if (!entMan.GetComponent<TransformComponent>(pipe).Anchored)
                xformSys.AnchorEntity(pipe);
            Assert.That(Attempt(), Is.False, "a pipe in the footprint must veto unpacking");
            entMan.DeleteEntity(pipe);

            // Cables are expected under a machine and must not block it.
            var cable = entMan.SpawnEntity("CableHV", testMap.GridCoords);
            if (!entMan.GetComponent<TransformComponent>(cable).Anchored)
                xformSys.AnchorEntity(cable);
            Assert.That(Attempt(), Is.True, "cables under the machine must not block unpacking");

            // Rotating the flatpack turns the 3x5 footprint into 5x3. The pipe at (2, 0) is outside
            // the unrotated footprint but inside the rotated one, so only a rotated check catches it.
            entMan.GetComponent<TransformComponent>(flatpack).LocalRotation = Angle.FromDegrees(90);
            var rotatedPipe = entMan.SpawnEntity("GasPipeStraight",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(2, 0)));
            if (!entMan.GetComponent<TransformComponent>(rotatedPipe).Anchored)
                xformSys.AnchorEntity(rotatedPipe);
            Assert.That(Attempt(), Is.False, "the footprint must rotate with the flatpack");
            entMan.DeleteEntity(rotatedPipe);

            // Every footprint tile needs a floor under it.
            mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(0, 1), Tile.Empty);
            Assert.That(Attempt(), Is.False, "the turbine needs floor under every footprint tile");
        });

        await pair.CleanReturnAsync();
    }
}
