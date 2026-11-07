using System;
using System.Linq;
using Content.Server._NSV13.Reactor.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     Guards the two things a player cannot easily check themselves: that the RBMK flatpack really
///     lays the footing, cable mesh and three pipe stubs it advertises, and that the unpacked reactor
///     remembers it can be packed up again.
/// </summary>
[TestFixture]
public sealed class ReactorFlatpackTest
{
    [Test]
    public async Task RbmkFlatpackLaysFootingCablesAndPipes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapSys = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            // The test map ships one plating tile at the origin; give the 3x3 footing somewhere to go.
            var plating = tiles["Plating"];
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            var flatpackProto = protoMan.Index<EntityPrototype>("RbmkReactorFlatpack");
            Assert.That(flatpackProto.TryGetComponent<FlatpackComponent>(out var flatpackComp), Is.True);
            Assert.That(flatpackComp!.UnpackDelay, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(flatpackProto.TryGetComponent<ReactorFlatpackComponent>(out var site), Is.True);
            Assert.That(site!.Footprint, Is.EqualTo(new Vector2i(3, 3)));

            var flatpack = entMan.SpawnEntity("RbmkReactorFlatpack", testMap.GridCoords);
            var reactor = entMan.SpawnEntity("RbmkNuclearReactor", testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(reactor, new FlatpackUnpackedEvent(flatpack, EntityUid.Invalid, reactor), true);

            Assert.That(entMan.TryGetComponent<ReactorAssemblyComponent>(reactor, out var assembly), Is.True,
                "the reactor must remember it came out of a flatpack");
            Assert.That(assembly!.Parts.Count, Is.EqualTo(12), "nine HV cables and three pipe stubs");
            Assert.That(entMan.GetComponent<TransformComponent>(reactor).Anchored, Is.True,
                "the reactor has to be bolted down or its nodes never join a net");

            var center = mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp, testMap.GridCoords);
            var reinforced = tiles["FloorReinforced"].TileId;

            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var tile = mapSys.GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(x, y));
                    Assert.That(tile.Tile.TypeId, Is.EqualTo((int) reinforced),
                        $"the flatpack's footing is missing at {center + new Vector2i(x, y)}");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
