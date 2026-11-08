using System.Collections.Generic;
using System.Linq;
using Content.Server._NF.DangerTether;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Server.Shuttles.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Server.Construction.Components;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Atmos;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Content.Shared._EE.Supermatter.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Examine;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
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
[TestOf(typeof(Content.Server._NSV13.Reactor.Systems.RbmkReactorSystem))]
public sealed class ReactorHardwareTest
{
    private const string Rbmk = "RbmkNuclearReactor";
    private const string Stormdrive = "StormdriveReactor";

    /// <summary>
    ///     Both reactors must present an HV take-off, a supplier that can actually ramp, and no way
    ///     to unbolt them.
    /// </summary>
    [Test]
    [TestCase(Rbmk)]
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
    ///     The supermatter shard is the movable, purchasable crystal: it has to spawn unanchored and
    ///     keep both anchor flags, or it cannot be carried aboard and bolted down.
    /// </summary>
    [Test]
    public async Task SupermatterShardIsMovableAndOnSale()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("SupermatterSmall", testMap.GridCoords);

            Assert.That(entMan.GetComponent<TransformComponent>(uid).Anchored, Is.False,
                "the shard ships loose so it can be dragged around");

            // Same [Access] restriction as above: assert the wiring, not the flags.
            Assert.That(entMan.HasComponent<AnchorableComponent>(uid), Is.True,
                "the shard must be wrenchable, or it cannot be installed aboard a ship");

            // Dragging in this engine is pull-based, and pulling needs Pullable. The crystal does not
            // inherit it from BaseItem/BaseStructure like every other movable thing does.
            Assert.That(entMan.HasComponent<PullableComponent>(uid), Is.True,
                "the shard must be pullable, or it cannot be dragged onto a ship");

            // Pulling rejects static bodies. The parent's Physics defaults to Static and only the
            // anchoring system flips it to Dynamic on an explicit unanchor, so a freshly spawned
            // shard could not be dragged until it was wrenched down and back up.
            Assert.That(entMan.GetComponent<PhysicsComponent>(uid).BodyType, Is.EqualTo(BodyType.Dynamic),
                "the shard must spawn with a dynamic body or it cannot be pulled");

            // Same crystal as the full-size one: identical failure modes and output.
            var small = entMan.GetComponent<SupermatterComponent>(uid);

            var fullProto = protoMan.Index<EntityPrototype>("Supermatter");
            Assert.That(fullProto.TryGetComponent<SupermatterComponent>(out var full), Is.True);

            // The EE supermatter has no separate delamination threshold. What the shard shares with
            // the full crystal is the failure timing; what it deliberately does not share is the
            // sample size and the blast (the SS13 shard is a smaller, weaker version of the same
            // engine), so pin both facts rather than claiming the two are identical.
            Assert.That(small.DelamTimer, Is.EqualTo(full!.DelamTimer), "the shard uses the same fuse length");
            Assert.That(small.GasEfficiency, Is.LessThan(full.GasEfficiency),
                "the shard samples less gas than the full crystal");
            Assert.That(small.DelamRadiusMultiplier, Is.LessThan(full.DelamRadiusMultiplier),
                "the shard's delamination blast is smaller than the full crystal's");

            Assert.That(protoMan.HasIndex<EntityPrototype>("SupermatterSmallFlatpack"), Is.True,
                "the shard needs a flatpack to be sellable");
            Assert.That(protoMan.HasIndex<CargoProductPrototype>("EngineSupermatterShard"), Is.True,
                "the shard should be purchasable through cargo");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The flatpacks ship without rods, and rods wear out, so both kinds have to be buyable.
    ///     One crate per type, five rods inside - a full load for either machine.
    /// </summary>
    [Test]
    public async Task ReactorConsumablesAreSoldByCargo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            (string Product, string Crate, string Rod, int Cost)[] catalogue =
            [
                ("EngineReactorControlRods", "CrateReactorControlRods", "ControlRod", 8000),
                ("EngineRbmkFuelRods", "CrateRbmkFuelRods", "RbmkFuelRodUranium", 10000),
            ];

            var containers = entMan.System<SharedContainerSystem>();

            foreach (var (productId, crateId, rodId, cost) in catalogue)
            {
                Assert.That(protoMan.TryIndex<CargoProductPrototype>(productId, out var product), Is.True,
                    $"{productId} is missing from the cargo catalogue");
                Assert.That(product!.Product.Id, Is.EqualTo(crateId),
                    $"{productId} should sell {crateId}");
                Assert.That(product.Cost, Is.EqualTo(cost));

                var crate = entMan.SpawnEntity(crateId, testMap.GridCoords);

                Assert.That(containers.TryGetContainer(crate, SharedEntityStorageSystem.ContainerName, out var storage),
                    Is.True, $"{crateId} is not a crate");

                Assert.That(storage!.ContainedEntities.Count, Is.EqualTo(5),
                    $"{crateId} should carry exactly five rods");

                foreach (var rod in storage.ContainedEntities)
                {
                    Assert.That(entMan.GetComponent<MetaDataComponent>(rod).EntityPrototype?.ID,
                        Is.EqualTo(rodId), $"{crateId} should contain only {rodId}");
                }

                entMan.DeleteEntity(crate);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     A supermatter collapse that eats the floor out from under itself must not lose the
    ///     singularity to the danger tether: the ship grid is the tether, and it is still the ship
    ///     grid even after the tile below is gone.
    /// </summary>
    [Test]
    public async Task SingularityOnAShipSurvivesLosingItsFloor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = testMap.Grid.Owner;
        EntityUid singularity = default;

        await server.WaitAssertion(() =>
        {
            // Test maps are not guaranteed to be shuttles; a real ship grid always is.
            entMan.EnsureComponent<ShuttleComponent>(grid);

            // Give the grid a deck rather than a single tile: a one-tile grid is deleted when its
            // last tile goes, which would delete the singularity with it and prove nothing.
            var map = entMan.System<SharedMapSystem>();
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);
            for (var x = -4; x <= 4; x++)
            {
                for (var y = -4; y <= 4; y++)
                    map.SetTile(grid, gridComp, new Vector2i(x, y), new Tile(1));
            }

            singularity = entMan.SpawnEntity("Singularity", testMap.GridCoords);

            var tether = entMan.System<DangerTetherSystem>();
            Assert.That(tether.AnyTetherInRange(singularity), Is.True,
                "a singularity on a ship grid must count as tethered");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var map = entMan.System<SharedMapSystem>();
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);

            // The event horizon eats the tile it is standing on. Removing a tile deparents everything
            // on it to the map, so the singularity is now "in space" over its own hole - it must
            // still count as the ship's problem, or the tether culls it half a second later.
            map.SetTile(grid, gridComp, Vector2i.Zero, Tile.Empty);

            Assert.That(entMan.EntityExists(singularity), Is.True,
                "removing a tile must not delete the singularity");

            var tether = entMan.System<DangerTetherSystem>();
            Assert.That(tether.AnyTetherInRange(singularity), Is.True,
                "a danger hovering over the hole it ate is still tethered to the ship");
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(singularity), Is.True,
                "the danger tether must not cull the singularity for eating its own floor");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The reactor footprint must reserve its tiles against girders and walls without turning
    ///     the machine into a wall itself.
    /// </summary>
    [Test]
    [TestCase("RbmkNuclearReactor", 1.5f, 1.5f, 2.5f, 2.5f, false)]
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
    ///     The examine overlay takes its faces from <see cref="ReactorPortLayoutComponent"/>, not
    ///     from the pipe nodes - those are <c>serverOnly</c>, so the client never sees them. That
    ///     leaves two copies of the same fact, and a silent drift would point the arrows at the
    ///     wrong face of a machine whose art gives no hint either way.
    /// </summary>
    [Test]
    [TestCase(Rbmk)]
    [TestCase(Stormdrive)]
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

            if (protoId == Rbmk)
            {
                AssertFace(nodes, "coolantIn", layout.CoolantIn);
                AssertFace(nodes, "coolantOut", layout.CoolantOut);
                AssertFace(nodes, "moderatorIn", layout.Moderator);
            }
            else
            {
                AssertFace(nodes, "fuel", layout.Fuel);
                AssertFace(nodes, "waste", layout.Waste);
            }

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

            if (protoId == Rbmk)
            {
                Assert.That(layout.CoolantIn, Is.EqualTo(Direction.West), "client-side coolant inlet face");
                Assert.That(layout.CoolantOut, Is.EqualTo(Direction.East), "client-side coolant outlet face");
                Assert.That(layout.Moderator, Is.EqualTo(Direction.North), "client-side moderator face");
            }
            else
            {
                Assert.That(layout.Fuel, Is.EqualTo(Direction.East), "client-side fuel face");
                Assert.That(layout.Waste, Is.EqualTo(Direction.West), "client-side waste face");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The reactor prints are engineering work, not mercenary work. They were accidentally filed
    ///     in the mercenary/NFSD blueprint pack, so the lithograph only offered them under the
    ///     Mercenary discipline and an engineering techfab had no design to insert.
    /// </summary>
    /// <remarks>
    ///     The split also pins what each node is for: Fission Basics is the consumables and repair
    ///     kit, Nuclear Fission is the two machines and their consoles. Neither node takes a power
    ///     technology as a prerequisite; every route to actually running a reactor needs a power net
    ///     anyway, and the extra edge only stretched a line across the tree.
    /// </remarks>
    [Test]
    public async Task ReactorPrintsAreEngineeringBlueprints()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitPost(() =>
        {
            string[] maintenance =
            {
                "ControlRodInferior",
                "ReactorSealant",
                "RbmkFuelRodUranium",
                "StormdriveCore",
            };

            string[] reactors =
            {
                "RbmkReactorFlatpack",
                "RbmkConsoleCircuitboard",
                "StormdriveReactorFlatpack",
                "StormdriveConsoleCircuitboard",
            };

            var engineering = proto.Index<LatheRecipePackPrototype>("NFBlueprintsEngineering")
                .Recipes.Select(it => (string) it).ToHashSet();
            var mercenary = proto.Index<LatheRecipePackPrototype>("NFBlueprintsMercenaryNfsd")
                .Recipes.Select(it => (string) it).ToHashSet();
            var basics = proto.Index<TechnologyPrototype>("NFReactorControlRods");
            var fission = proto.Index<TechnologyPrototype>("NFStormdriveMachinery");
            var basicsUnlocks = basics.RecipeUnlocks.Select(it => (string) it).ToHashSet();
            var fissionUnlocks = fission.RecipeUnlocks.Select(it => (string) it).ToHashSet();
            var basicsPrereqs = basics.TechnologyPrerequisites.Select(it => (string) it).ToHashSet();
            var fissionPrereqs = fission.TechnologyPrerequisites.Select(it => (string) it).ToHashSet();
            var portableParticle = proto.Index<TechnologyPrototype>("NFParticleDecelerator");
            var particlePrereqs = portableParticle.TechnologyPrerequisites.Select(it => (string) it).ToHashSet();

            Assert.Multiple(() =>
            {
                foreach (var recipe in maintenance.Concat(reactors))
                {
                    Assert.That(engineering, Contains.Item(recipe),
                        $"{recipe} must be printable as an engineering blueprint");
                    Assert.That(mercenary, Does.Not.Contain(recipe),
                        $"{recipe} must not be a mercenary blueprint");
                }

                Assert.That(basicsUnlocks, Is.SupersetOf(maintenance),
                    "Fission Basics must unlock the consumables and repair kit");
                Assert.That(fissionUnlocks, Is.SupersetOf(reactors),
                    "Nuclear Fission must unlock both reactors and both consoles");

                Assert.That(basicsPrereqs, Contains.Item("NFParticleDecelerator"),
                    "Fission Basics follows Portable Particle Emission: the handheld emitter is " +
                    "unlocked first, then the reactor branch opens");
                Assert.That(basicsPrereqs, Does.Not.Contain("NFAdvancedPowercells"),
                    "Fission Basics must not take a power technology as a prerequisite");
                Assert.That(fissionPrereqs, Does.Not.Contain("NFAdvancedPowercells"),
                    "Nuclear Fission must not take a power technology as a prerequisite");
                Assert.That(particlePrereqs, Does.Not.Contain("NFReactorControlRods"),
                    "Portable Particle Emission must not be gated behind the reactor branch");

                var core = proto.Index<EntityPrototype>("StormdriveCore");
                Assert.That(core.Components, Does.ContainKey("MultiHandedItem"),
                    "a live reactor core must be carried with both hands");
            });
        });

        await pair.CleanReturnAsync();
    }
}
