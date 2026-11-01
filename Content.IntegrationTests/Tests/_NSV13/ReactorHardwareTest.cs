using System.Linq;
using Content.Server._NF.DangerTether;
using Content.Server.NodeContainer;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Server.Construction.Components;
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
using Robust.Shared.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

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
[TestOf(typeof(Content.Server._NSV13.Reactor.Systems.StormdriveSystem))]
public sealed class ReactorHardwareTest
{
    private const string Stormdrive = "StormdriveReactor";

    /// <summary>
    ///     Both reactors must present an HV take-off, a supplier that can actually ramp, and no way
    ///     to unbolt them.
    /// </summary>
    [Test]
    // [TestCase(Rbmk)] // BRANCH NOTE (stormdrive branch): the RBMK lives on its own branch.
    [TestCase(Stormdrive)]
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
            reactor = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);

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
    [TestCase("StormdriveReactor", 1.5f, 0.5f, 0.5f, 1.5f, true)]
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

    /// <summary>
    ///     Port sides are given in the examiner's screen frame: a drive turned a quarter turn must
    ///     report its fuel/waste faces as up/down, not left/right.
    /// </summary>
    [Test]
    public async Task RotatedStormdriveReportsPortsInTheViewersFrame()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var xform = entMan.System<SharedTransformSystem>();
            var examiner = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var drive = entMan.SpawnEntity("StormdriveReactor", testMap.GridCoords);

            // A quarter turn: local east/west now point up/down on the examiner's screen.
            // BaseMachine sets Transform.noRot, which blocks runtime rotation; mapped reactors get
            // their rotation from the serialized transform instead. Lift it to simulate that.
            entMan.GetComponent<TransformComponent>(drive).NoLocalRotation = false;
            xform.SetLocalRotation(drive, Angle.FromDegrees(90));
            Assert.That(entMan.GetComponent<TransformComponent>(drive).LocalRotation.Theta,
                Is.EqualTo(Math.PI / 2).Within(0.01),
                "SetLocalRotation must set the component");
            Assert.That(xform.GetWorldRotation(drive).Theta, Is.EqualTo(Math.PI / 2).Within(0.01),
                "the drive must actually be a quarter turn around");

            var message = new FormattedMessage();
            var ev = new ExaminedEvent(message, drive, examiner, true, false);
            entMan.EventBus.RaiseLocalEvent(drive, ev);

            var text = ev.GetTotalMessage().ToMarkup();
            var driveRot = xform.GetWorldRotation(drive);
            var viewerGridUid = entMan.GetComponent<TransformComponent>(examiner).GridUid;
            var viewerRot = viewerGridUid is { } vg ? xform.GetWorldRotation(vg) : Angle.Zero;
            var fuelSide = Content.Server._NSV13.Reactor.ReactorExamine.ScreenSide(
                driveRot, viewerRot, Direction.East);
            var context = $"drive={driveRot.Theta:F3} viewer={viewerRot.Theta:F3} fuel={fuelSide} text={text}";
            Assert.That(text, Does.Contain("up"), $"fuel face after a quarter turn should be up: {context}");
            Assert.That(text, Does.Contain("down"), $"waste face after a quarter turn should be down: {context}");
        });

        await pair.CleanReturnAsync();
    }
}
