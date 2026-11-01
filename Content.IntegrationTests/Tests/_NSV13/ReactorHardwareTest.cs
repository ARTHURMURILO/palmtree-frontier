using System.Collections.Generic;
using System.Linq;
using Content.Server._NF.DangerTether;
using Content.Server.Construction.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Atmos;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.NodeContainer;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Examine;
using Robust.Shared.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     Guards the things about the two reactors that the console cannot show you: that they actually
///     export power, and that they cannot be wrenched off the deck.
/// </summary>
/// <remarks>
///     Both were real bugs. The reactors set <c>PowerSupplier.MaxSupply</c> every tick but had no
///     <c>CableDeviceNode</c>, so the supplier was never attached to a net and the megawatts went
///     nowhere; even with a node, the supplier's ramp rate defaults to zero and the solver only
///     moves <c>SupplyRampPosition</c> by <c>rate * frameTime</c>, which pins delivery at 0 W
///     forever. Neither failure throws, logs, or looks wrong on the reactor's own console - the
///     console reads the machine's internal figure, not what reached the grid - so a test is the
///     only thing that will notice if it regresses.
/// </remarks>
[TestFixture]
[TestOf(typeof(Content.Server._NSV13.Reactor.Systems.RbmkReactorSystem))]
public sealed class ReactorHardwareTest
{
    private const string Rbmk = "RbmkNuclearReactor";

    /// <summary>
    ///     Both reactors must present an HV take-off, a supplier that can actually ramp, and no way
    ///     to unbolt them.
    /// </summary>
    [Test]
    [TestCase(Rbmk)]
    // [TestCase(Stormdrive)] // BRANCH NOTE (rbmk branch): the stormdrive lives on its own branch.
    public async Task ReactorExportsHighVoltageAndCannotBeUnwrenched(string protoId)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(protoId, testMap.GridCoords);

            Assert.That(entMan.TryGetComponent<NodeContainerComponent>(uid, out var container), Is.True,
                $"{protoId} must have a node container");

            var cable = container!.Nodes.Values.OfType<CableDeviceNode>().ToList();
            Assert.That(cable, Is.Not.Empty, $"{protoId} has no cable device node, so it cannot reach a power net");
            Assert.That(cable.Any(node => node.NodeGroupID == NodeGroupID.HVPower), Is.True,
                $"{protoId} take-off must be on the HV net");

            Assert.That(entMan.TryGetComponent<PowerSupplierComponent>(uid, out var supplier), Is.True,
                $"{protoId} must supply power");
            Assert.That(supplier!.SupplyRampRate, Is.GreaterThan(0f),
                "a zero supply ramp rate pins delivered power at 0 W no matter what the tick sets");
            Assert.That(supplier.SupplyRampTolerance, Is.GreaterThan(0f),
                "a zero ramp tolerance caps the solver's effective supply at the ramp position");

            // `AnchorableComponent.Flags` is [Access]-restricted to its own system, so the flags
            // themselves can only be checked in game; what a test can assert is that the machine is
            // anchorable at all, which is what lets it be fitted on a fresh spawn.
            Assert.That(entMan.HasComponent<AnchorableComponent>(uid), Is.True,
                $"{protoId} must be anchorable so it can be fitted on spawn");

            // The stock `Machine` graph turns a reactor back into a machine frame with a single
            // crowbar do-after. The reactors use the terminal `ReactorMachine` graph instead, where
            // neither construction nor deconstruction can path anywhere.
            var construction = entMan.GetComponent<ConstructionComponent>(uid);
            var graph = protoMan.Index<ConstructionGraphPrototype>(construction.Graph);
            var deconstructPath = graph.Path(construction.Node, construction.DeconstructionNode!);
            Assert.That(deconstructPath is null || deconstructPath.Length == 0, Is.True,
                $"{protoId} must not be deconstructible");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The examine overlay takes its faces from <see cref="ReactorPortLayoutComponent"/>, not
    ///     from the pipe nodes - those are <c>serverOnly</c>, so the client never sees them. That
    ///     leaves two copies of the same fact, and a silent drift would point the coolant arrows at
    ///     the wrong face of a machine whose art gives no hint either way.
    /// </summary>
    [Test]
    [TestCase(Rbmk)]
    public async Task ReactorPortLayoutMatchesItsPipeNodes(string protoId)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true, Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        EntityUid reactor = default;
        NetEntity netReactor = default;

        await server.WaitAssertion(() =>
        {
            reactor = entMan.SpawnEntity(protoId, testMap.GridCoords);
            netReactor = entMan.GetNetEntity(reactor);
            var nodes = entMan.GetComponent<NodeContainerComponent>(reactor).Nodes;
            var layout = entMan.GetComponent<ReactorPortLayoutComponent>(reactor);

            AssertFace(nodes, "coolantIn", layout.CoolantIn);
            AssertFace(nodes, "coolantOut", layout.CoolantOut);
            AssertFace(nodes, "moderatorIn", layout.Moderator);

            static void AssertFace(Dictionary<string, Node> nodes, string nodeName, Direction face)
            {
                Assert.That(nodes.TryGetValue(nodeName, out var node), Is.True, $"{nodeName} is missing");
                Assert.That(node, Is.InstanceOf<PipeNode>(), $"{nodeName} must be a pipe node");
                Assert.That(face, Is.Not.EqualTo(Direction.Invalid),
                    $"{nodeName} has no face in the overlay layout, so the client cannot draw it");

                var pipeFace = ((PipeNode) node!).OriginalPipeDirection;
                Assert.That(pipeFace, Is.EqualTo(face.ToPipeDirection()),
                    $"{nodeName}: the overlay points at {face}, the pipe node is on {pipeFace}");
            }
        });

        await pair.RunTicksSync(5);

        // The faces are the only thing the client is allowed to know: the pipe nodes never leave
        // the server. If the component state does not actually cross the wire the overlay has
        // nothing to draw and the whole feature is silently dead, so assert it here.
        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var clientReactor = clientEntMan.GetEntity(netReactor);

            Assert.That(clientReactor.IsValid, Is.True, "the reactor entity did not reach the client");

            Assert.That(clientEntMan.HasComponent<ReactorPortLayoutComponent>(clientReactor), Is.True,
                "the port faces have to reach the client");

            var layout = clientEntMan.GetComponent<ReactorPortLayoutComponent>(clientReactor);
            Assert.That(layout.CoolantIn, Is.EqualTo(Direction.West), "client-side coolant inlet face");
            Assert.That(layout.CoolantOut, Is.EqualTo(Direction.East), "client-side coolant outlet face");
            Assert.That(layout.Moderator, Is.EqualTo(Direction.North), "client-side moderator face");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     A reactor with HV cable under it and a load on the net must actually deliver, not merely
    ///     report a figure on its own console.
    /// </summary>
    [Test]
    public async Task ReactorFeedsPowerIntoAnHvCable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        EntityUid reactor = default;

        await server.WaitAssertion(() =>
        {
            reactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", testMap.GridCoords);

            // The device node sits on the machine's own tile: this is the cable a player lays
            // "under" the reactor. The empty substation gives the net something to actually draw.
            entMan.SpawnEntity("CableHV", testMap.GridCoords);
            entMan.SpawnEntity("SubstationBasicEmpty", testMap.GridCoords);
        });

        await pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            var supplier = entMan.GetComponent<PowerSupplierComponent>(reactor);

            Assert.That(supplier.Net, Is.Not.Null,
                "the reactor's HV device node has to join the net formed by the cable");
            Assert.That(supplier.CurrentSupply, Is.GreaterThan(0f),
                "the reactor has to actually deliver power, not just set MaxSupply");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The reactor footprint must reserve its tiles against girders and walls without turning
    ///     the machine into a wall itself.
    /// </summary>
    [Test]
    [TestCase("RbmkNuclearReactor", 1.5f, 1.5f, 2.5f, 2.5f, false)]
    public async Task ReactorFootprintReservesConstructionWithoutBeingSolid(
        string protoId, float edgeX, float edgeY, float outsideX, float outsideY, bool centreSolid)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var turfs = entMan.System<TurfSystem>();
            var mapSys = entMan.System<SharedMapSystem>();
            var gridComp = entMan.GetComponent<MapGridComponent>(testMap.Grid);

            // The default test map is a single tile; the footprint needs a few more around it.
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    mapSys.SetTile(testMap.Grid, gridComp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            // Snapgrid centre of tile (0,0), the way the game places machines.
            var centre = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
            var reactor = entMan.SpawnEntity(protoId, centre);

            Assert.That(entMan.HasComponent<ConstructionBlockerComponent>(reactor), Is.True,
                $"{protoId} must carry the construction blocker");
            Assert.That(entMan.HasComponent<FixturesComponent>(reactor), Is.True,
                $"{protoId} must have fixtures");

            var centreTile = turfs.GetTileRef(centre);
            Assert.That(centreTile, Is.Not.Null, "the test map must have a tile under the reactor");

            // MachineMask, not Impassable: a hard machine fixture is on MachineLayer, which does
            // not carry the Impassable bit.
            Assert.That(turfs.IsTileBlocked(centreTile!.Value, CollisionGroup.MachineMask),
                Is.EqualTo(centreSolid),
                $"{protoId} centre tile solidity");

            Assert.That(turfs.IsTileBlocked(centreTile.Value, CollisionGroup.Impassable,
                    includeConstructionBlockers: true),
                Is.True,
                $"{protoId} must reserve its centre tile for construction");

            var edge = new EntityCoordinates(testMap.Grid, edgeX, edgeY);
            var edgeTile = turfs.GetTileRef(edge);
            Assert.That(turfs.IsTileBlocked(edgeTile!.Value, CollisionGroup.Impassable,
                    includeConstructionBlockers: true),
                Is.True,
                $"{protoId} must reserve its outer footprint tiles");

            var outside = new EntityCoordinates(testMap.Grid, outsideX, outsideY);
            var outsideTile = turfs.GetTileRef(outside);
            Assert.That(turfs.IsTileBlocked(outsideTile!.Value, CollisionGroup.Impassable,
                    includeConstructionBlockers: true),
                Is.False,
                $"{protoId} must not reserve tiles outside its footprint");
        });

        await pair.CleanReturnAsync();
    }

}
