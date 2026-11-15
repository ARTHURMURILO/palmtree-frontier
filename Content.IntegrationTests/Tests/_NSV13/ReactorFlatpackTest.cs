using System;
using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Construction.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server._NSV13.Reactor.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.DoAfter;
using Content.Shared.NodeContainer;
using Content.Shared.Power;
using Content.Shared._NSV13.Reactor;
using Robust.Shared.Containers;
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
            Assert.That(flatpackComp!.UnpackDelay, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(flatpackProto.TryGetComponent<ReactorFlatpackComponent>(out var site), Is.True);
            Assert.That(site!.Footprint, Is.EqualTo(new Vector2i(3, 3)));
            Assert.That(site.DeconstructDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));

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

            // MachineComponent silently fills these on MapInit. A flatpack is the finished reactor,
            // so packing it must not hand out a free board and stock parts.
            var machineContainers = entMan.GetComponent<ContainerManagerComponent>(reactor).Containers;
            Assert.That(machineContainers[MachineFrameComponent.BoardContainerName].Count, Is.Zero,
                "the flatpack must not contain a free reactor circuitboard");
            Assert.That(machineContainers[MachineFrameComponent.PartContainerName].Count, Is.Zero,
                "the flatpack must not contain free machine stock parts");

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

    [Test]
    public async Task RbmkFlatpackVetoesFinishedFloorPrimaryPipesAndAtmosDevices()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapSys = server.System<SharedMapSystem>();
        var xformSys = server.System<SharedTransformSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            var plating = tiles["Plating"];
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            var flatpack = entMan.SpawnEntity("RbmkReactorFlatpack", testMap.GridCoords);
            var center = mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp, testMap.GridCoords);
            var obstacle = center + new Vector2i(1, 1);

            FlatpackUnpackAttemptEvent Attempt()
            {
                var ev = new FlatpackUnpackAttemptEvent(flatpack, EntityUid.Invalid, testMap.Grid.Owner, center);
                entMan.EventBus.RaiseLocalEvent(flatpack, ref ev);
                return ev;
            }

            Assert.That(Attempt().Cancelled, Is.False, "bare plating should allow the unpack");

            // A finished floor above the plating is exactly the case the crew must clear first.
            mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, obstacle, new Tile(tiles["FloorSteel"].TileId));
            var floored = Attempt();
            Assert.That(floored.Cancelled, Is.True, "a finished floor must veto the unpack");
            Assert.That(floored.Message, Is.Not.Null);

            mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, obstacle, new Tile(plating.TileId));

            // A crate left on the footing is unanchored, but the reactor would swallow it.
            var crate = entMan.SpawnEntity("CrateGenericSteel",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, obstacle));
            Assert.That(Attempt().Cancelled, Is.True, "an unanchored crate must veto the unpack");
            entMan.DeleteEntity(crate);

            var pipe = entMan.SpawnEntity("GasPipeStraight",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, obstacle));
            if (!entMan.GetComponent<TransformComponent>(pipe).Anchored)
                xformSys.AnchorEntity(pipe);

            Assert.That(Attempt().Cancelled, Is.True, "primary-layer piping must veto the unpack");

            var pipeNode = entMan.GetComponent<NodeContainerComponent>(pipe).Nodes.Values
                .OfType<PipeNode>()
                .Single();
            pipeNode.CurrentPipeLayer = AtmosPipeLayer.Secondary;
            Assert.That(Attempt().Cancelled, Is.False, "off-layer piping must not veto the unpack");

            entMan.DeleteEntity(pipe);

            var pump = entMan.SpawnEntity("GasPressurePump",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, obstacle));
            if (!entMan.GetComponent<TransformComponent>(pump).Anchored)
                xformSys.AnchorEntity(pump);

            var pumpLayers = entMan.GetComponent<AtmosPipeLayersComponent>(pump);
            server.System<AtmosPipeLayersSystem>().SetPipeLayer((pump, pumpLayers), AtmosPipeLayer.Secondary);
            Assert.That(pumpLayers.CurrentPipeLayer, Is.EqualTo(AtmosPipeLayer.Secondary),
                "the pump should be on the secondary layer for this check");

            var device = Attempt();
            Assert.That(device.Cancelled, Is.True, "an atmos device must veto the unpack even off the primary layer");
            Assert.That(device.Message, Is.Not.Null);

            entMan.DeleteEntity(pump);

            // The stormdrive is a 3x1 body. Its site check has to cover both end tiles, not just the
            // centre tile the flatpack itself lands on.
            var driveProto = protoMan.Index<EntityPrototype>("StormdriveReactorFlatpack");
            Assert.That(driveProto.TryGetComponent<ReactorFlatpackComponent>(out var driveSite), Is.True);
            Assert.That(driveSite!.Footprint, Is.EqualTo(new Vector2i(3, 1)));

            var driveFlatpack = entMan.SpawnEntity("StormdriveReactorFlatpack", testMap.GridCoords);
            var drivePipe = entMan.SpawnEntity("GasPipeStraight",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(1, 0)));
            if (!entMan.GetComponent<TransformComponent>(drivePipe).Anchored)
                xformSys.AnchorEntity(drivePipe);

            var driveAttempt = new FlatpackUnpackAttemptEvent(driveFlatpack, EntityUid.Invalid,
                testMap.Grid.Owner, center);
            entMan.EventBus.RaiseLocalEvent(driveFlatpack, ref driveAttempt);
            Assert.That(driveAttempt.Cancelled, Is.True,
                "primary-layer piping on a stormdrive end tile must veto the unpack");

            // Turning the flatpack turns the 3x1 body with it, so the check has to turn too.
            entMan.DeleteEntity(drivePipe);
            xformSys.SetLocalRotation(driveFlatpack, Direction.East.ToAngle());
            var driveNorthPipe = entMan.SpawnEntity("GasPipeStraight",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(0, 1)));
            if (!entMan.GetComponent<TransformComponent>(driveNorthPipe).Anchored)
                xformSys.AnchorEntity(driveNorthPipe);

            var turnedAttempt = new FlatpackUnpackAttemptEvent(driveFlatpack, EntityUid.Invalid,
                testMap.Grid.Owner, center);
            entMan.EventBus.RaiseLocalEvent(driveFlatpack, ref turnedAttempt);
            Assert.That(turnedAttempt.Cancelled, Is.True,
                "the rotated stormdrive's north end tile must be checked too");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RbmkFlatpackPackingDeletesHiddenMachineParts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = server.System<SharedMapSystem>();
        var containerSys = server.System<SharedContainerSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid board = default;
        EntityUid part = default;

        await server.WaitAssertion(() =>
        {
            var plating = tiles["Plating"];
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            var flatpack = entMan.SpawnEntity("RbmkReactorFlatpack", testMap.GridCoords);
            var reactor = entMan.SpawnEntity("RbmkNuclearReactor", testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(reactor,
                new FlatpackUnpackedEvent(flatpack, EntityUid.Invalid, reactor), true);

            // Simulate a reactor unpacked before the strip fix, or loaded from an old save: it still
            // has the board and stock parts MachineComponent created inside it.
            var containers = entMan.GetComponent<ContainerManagerComponent>(reactor).Containers;
            board = entMan.SpawnEntity("RbmkReactorMachineCircuitboard", testMap.GridCoords);
            part = entMan.SpawnEntity("CapacitorStockPart", testMap.GridCoords);
            Assert.That(containerSys.Insert(board, containers[MachineFrameComponent.BoardContainerName]), Is.True);
            Assert.That(containerSys.Insert(part, containers[MachineFrameComponent.PartContainerName]), Is.True);

            entMan.DeleteEntity(flatpack);

            var center = mapSys.TileIndicesFor(testMap.Grid.Owner, testMap.Grid.Comp, testMap.GridCoords);
            var crate = entMan.SpawnEntity("CrateGenericSteel",
                mapSys.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, center + new Vector2i(1, 1)));

            var blocked = new ReactorDeconstructDoAfterEvent();
            var blockedArgs = new DoAfterArgs(entMan, reactor, TimeSpan.FromSeconds(30), blocked, reactor, target: reactor);
            blocked.DoAfter = new Content.Shared.DoAfter.DoAfter(0, blockedArgs, TimeSpan.Zero);
            entMan.EventBus.RaiseLocalEvent(reactor, blocked);
            Assert.That(entMan.IsQueuedForDeletion(reactor), Is.False,
                "a crate on the reactor's footing must block packing");

            entMan.DeleteEntity(crate);

            var ev = new ReactorDeconstructDoAfterEvent();
            var args = new DoAfterArgs(entMan, reactor, TimeSpan.FromSeconds(30), ev, reactor, target: reactor);
            ev.DoAfter = new Content.Shared.DoAfter.DoAfter(0, args, TimeSpan.Zero);
            entMan.EventBus.RaiseLocalEvent(reactor, ev);

            Assert.That(entMan.IsQueuedForDeletion(reactor), Is.True,
                "packing the reactor must delete it");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(board), Is.False,
                "the hidden reactor board must die with the reactor, not drop on the floor");
            Assert.That(entMan.EntityExists(part), Is.False,
                "the hidden stock part must die with the reactor, not drop on the floor");

            var flatpacks = entMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "RbmkReactorFlatpack");
            Assert.That(flatpacks, Is.EqualTo(1), "packing should return exactly one flatpack");
        });

        await pair.CleanReturnAsync();
    }
}
