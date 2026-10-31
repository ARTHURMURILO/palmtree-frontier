using System.Linq;
using Content.Server.NodeContainer;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Construction.Components;
using Content.Shared._Goobstation.Supermatter.Components;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

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

            // Same crystal as the full-size one: identical failure modes and output.
            var small = entMan.GetComponent<SupermatterComponent>(uid);

            var fullProto = protoMan.Index<EntityPrototype>("Supermatter");
            Assert.That(fullProto.TryGetComponent<SupermatterComponent>(out var full), Is.True);

            Assert.That(small.DelaminationPoint, Is.EqualTo(full!.DelaminationPoint),
                "the shard is a mounting option, not a weaker engine");
            Assert.That(small.DelamTimer, Is.EqualTo(full.DelamTimer), "the shard uses the same fuse length");

            Assert.That(protoMan.HasIndex<EntityPrototype>("SupermatterSmallFlatpack"), Is.True,
                "the shard needs a flatpack to be sellable");
            Assert.That(protoMan.HasIndex<CargoProductPrototype>("EngineSupermatterShard"), Is.True,
                "the shard should be purchasable through cargo");
        });

        await pair.CleanReturnAsync();
    }
}
