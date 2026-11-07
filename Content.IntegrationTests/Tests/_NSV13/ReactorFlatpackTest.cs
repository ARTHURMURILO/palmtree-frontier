using System;
using System.Linq;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server._NSV13.Reactor.Components;
using Content.Shared.Atmos;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.NodeContainer;
using Content.Shared.Power;
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
            var center = mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp, testMap.GridCoords);

            // An MV cable already running through the footprint must not stop the HV mesh being
            // laid: a skipped tile can leave the reactor's own take-off with no HV node.
            var mvCable = entMan.SpawnEntity("CableMV",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(1, 1)));

            // A pipe laid across a port must not make the flatpack skip its own stub: the two
            // nodes would not line up, leaving the port dead.
            var westPort = center + new Vector2i(-1, 0);
            var crossPipe = entMan.SpawnEntity("GasPipeStraight",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, westPort));
            if (!entMan.GetComponent<TransformComponent>(crossPipe).Anchored)
                server.System<SharedTransformSystem>().AnchorEntity(crossPipe);

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor", testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(reactor, new FlatpackUnpackedEvent(flatpack, EntityUid.Invalid, reactor), true);

            Assert.That(entMan.TryGetComponent<ReactorAssemblyComponent>(reactor, out var assembly), Is.True,
                "the reactor must remember it came out of a flatpack");
            Assert.That(assembly!.Parts.Count, Is.EqualTo(12), "nine HV cables and three pipe stubs");
            Assert.That(entMan.GetComponent<TransformComponent>(reactor).Anchored, Is.True,
                "the reactor has to be bolted down or its nodes never join a net");

            var hvCables = assembly.Parts
                .Where(p => entMan.TryGetComponent<CableComponent>(p, out var cable)
                            && cable.CableType == CableType.HighVoltage)
                .ToList();
            Assert.That(hvCables, Has.Count.EqualTo(9), "every footprint tile needs its own HV cable");
            Assert.That(entMan.HasComponent<CableComponent>(mvCable), Is.True,
                "the flatpack must not delete a cable that was already there");

            // The stubs have to be turned onto their own ports; copying the reactor's rotation
            // covers the north port by accident, but lays east and west across their ports.
            var pipeStubs = assembly.Parts
                .Where(p => entMan.TryGetComponent<NodeContainerComponent>(p, out var container)
                            && container.Nodes.Values.Any(node => node is PipeNode))
                .ToList();
            Assert.That(pipeStubs, Has.Count.EqualTo(3), "one straight stub on each of the three ports");
            Assert.That(pipeStubs.Any(p => mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp,
                    entMan.GetComponent<TransformComponent>(p).Coordinates) == westPort), Is.True,
                "a pipe across the west port must not stop the flatpack laying its own stub there");
            Assert.That(entMan.HasComponent<NodeContainerComponent>(crossPipe), Is.True,
                "the flatpack must not delete a pipe that was already there");

            foreach (var stub in pipeStubs)
            {
                var stubPos = mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp,
                    entMan.GetComponent<TransformComponent>(stub).Coordinates);
                var offset = stubPos - center;

                // Vector2i.GetCardinalDir() uses the raw atan2 angle, which is not the convention
                // Direction.ToAngle() uses; ToIntVec() is what the pipe nodes themselves offset by.
                var toStub = DirectionExtensions.AllDirections.Single(dir => dir.ToIntVec() == offset);
                var node = entMan.GetComponent<NodeContainerComponent>(stub).Nodes.Values
                    .OfType<PipeNode>()
                    .Single();

                Assert.That(node.CurrentPipeDirection.HasDirection(toStub.GetOpposite().ToPipeDirection()), Is.True,
                    $"the {toStub} stub at {stubPos} (centre {center}) is {node.CurrentPipeDirection}, " +
                    $"but its port needs {toStub.GetOpposite().ToPipeDirection()}");
            }

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
