using System;
using System.Linq;
using Content.Server.Cargo.Components;
using Content.Server._PS.Construction.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Construction.Components;
using Content.Shared._PS.Construction;
using Content.Shared.DoAfter;
using Content.Shared.Prototypes;
using Content.Shared.Research.Prototypes;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
///     Guards the TEG flatpack acquisition and pack-up loop: the two flatpacks have to point at
///     packable TEG parts, cargo has to ship a complete kit, Advanced Atmospherics has to unlock
///     both prints, and packing a part has to hand back exactly its own flatpack.
/// </summary>
[TestFixture]
public sealed class TegFlatpackTest
{
    [Test]
    public async Task FlatpackPrototypesAreWiredToPackableParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            AssertPackable(protoMan, "TegCirculatorFlatpack", "TegCirculatorFlatpacked");
            AssertPackable(protoMan, "TegCenterFlatpack", "TegCenterFlatpacked");

            Assert.That(protoMan.Index<EntityPrototype>("TegCirculator")
                    .HasComponent<PackableIntoFlatpackComponent>(), Is.False,
                "a map-spawned circulator must not be packable");
            Assert.That(protoMan.Index<EntityPrototype>("TegCenter")
                    .HasComponent<PackableIntoFlatpackComponent>(), Is.False,
                "a map-spawned TEG generator must not be packable");
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertPackable(IPrototypeManager protoMan, string flatpackId, string partId)
    {
        var flatpack = protoMan.Index<EntityPrototype>(flatpackId);
        Assert.That(flatpack.TryGetComponent<FlatpackComponent>(out var pack), Is.True,
            $"{flatpackId} is not a flatpack");
        Assert.That(pack!.Entity?.Id, Is.EqualTo(partId), $"{flatpackId} should unpack into {partId}");

        var part = protoMan.Index<EntityPrototype>(partId);
        Assert.That(part.TryGetComponent<PackableIntoFlatpackComponent>(out var packable), Is.True,
            $"{partId} must remember its flatpack");
        Assert.That(packable!.Flatpack.Id, Is.EqualTo(flatpackId));
        Assert.That(packable.PackDelay, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task TegCrateCarriesTwoCirculatorsAndOneGenerator()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var containerSys = server.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var crate = entMan.SpawnEntity("CrateTeg", testMap.GridCoords);

            Assert.That(containerSys.TryGetContainer(crate, SharedEntityStorageSystem.ContainerName, out var storage),
                Is.True, "the TEG crate is not an entity storage");

            var ids = storage!.ContainedEntities
                .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID)
                .ToList();

            Assert.That(ids, Has.Count.EqualTo(3), "the kit is exactly three flatpacks");
            Assert.That(ids.Count(id => id == "TegCirculatorFlatpack"), Is.EqualTo(2),
                "a TEG needs two circulators");
            Assert.That(ids.Count(id => id == "TegCenterFlatpack"), Is.EqualTo(1),
                "a TEG needs one generator");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CargoAndAdvancedAtmosphericsUnlockTheFlatpacks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var product = protoMan.Index<CargoProductPrototype>("EngineTeg");
            Assert.That(product.Product.Id, Is.EqualTo("CrateTeg"));
            Assert.That(product.Cost, Is.EqualTo(10000));

            var tech = protoMan.Index<TechnologyPrototype>("NFAdvancedAtmospherics");
            var unlocks = tech.RecipeUnlocks.Select(r => r.Id).ToList();
            Assert.That(unlocks, Contains.Item("TegCirculatorFlatpack"));
            Assert.That(unlocks, Contains.Item("TegCenterFlatpack"));

            var circulator = protoMan.Index<LatheRecipePrototype>("TegCirculatorFlatpack");
            Assert.That(circulator.Result?.Id, Is.EqualTo("TegCirculatorFlatpack"));
            Assert.That(circulator.Materials.Single(m => m.Key.Id == "Steel").Value, Is.EqualTo(1000));
            Assert.That(circulator.Materials.Single(m => m.Key.Id == "Glass").Value, Is.EqualTo(500));

            var center = protoMan.Index<LatheRecipePrototype>("TegCenterFlatpack");
            Assert.That(center.Result?.Id, Is.EqualTo("TegCenterFlatpack"));
            Assert.That(center.Materials.Single(m => m.Key.Id == "Steel").Value, Is.EqualTo(2000));
            Assert.That(center.Materials.Single(m => m.Key.Id == "Glass").Value, Is.EqualTo(1000));
            Assert.That(center.Materials.Single(m => m.Key.Id == "Plastic").Value, Is.EqualTo(500));

            // The recipes are cheap; the flatpacks must not be a lathe-to-cargo money printer.
            foreach (var flatpackId in new[] { "TegCirculatorFlatpack", "TegCenterFlatpack" })
            {
                var flatpack = protoMan.Index<EntityPrototype>(flatpackId);
                Assert.That(flatpack.HasComponent<CargoSellBlacklistComponent>(), Is.True,
                    $"{flatpackId} must not be sellable");
                Assert.That(flatpack.TryGetComponent<StaticPriceComponent>(out var price), Is.True);
                Assert.That(price!.Price, Is.Zero, $"{flatpackId} must not carry a sell price");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    [TestCase("TegCirculatorFlatpacked", "TegCirculatorFlatpack")]
    [TestCase("TegCenterFlatpacked", "TegCenterFlatpack")]
    public async Task PackingReturnsTheMatchingFlatpack(string partProto, string flatpackProto)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var xformSys = server.System<SharedTransformSystem>();

        EntityUid part = default;

        await server.WaitAssertion(() =>
        {
            part = entMan.SpawnEntity(partProto, testMap.GridCoords);
            xformSys.SetLocalRotation(part, Direction.East.ToAngle());

            var first = new PackIntoFlatpackDoAfterEvent();
            var args = new DoAfterArgs(entMan, part, TimeSpan.FromSeconds(5), first, part, target: part);
            first.DoAfter = new Content.Shared.DoAfter.DoAfter(0, args, TimeSpan.Zero);
            entMan.EventBus.RaiseLocalEvent(part, first);

            // Two players can finish on the same tick. The first already queued the part for
            // deletion, so the second must not hand out another flatpack.
            var second = new PackIntoFlatpackDoAfterEvent();
            var secondArgs = new DoAfterArgs(entMan, part, TimeSpan.FromSeconds(5), second, part, target: part);
            second.DoAfter = new Content.Shared.DoAfter.DoAfter(0, secondArgs, TimeSpan.Zero);
            entMan.EventBus.RaiseLocalEvent(part, second);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(part), Is.False, "packing must delete the deployed part");

            var flatpacks = entMan.EntityQuery<MetaDataComponent>()
                .Where(meta => meta.EntityPrototype?.ID == flatpackProto)
                .ToList();

            Assert.That(flatpacks, Has.Count.EqualTo(1), "packing must return exactly one matching flatpack");

            var flatpack = flatpacks[0].Owner;
            Assert.That(entMan.GetComponent<TransformComponent>(flatpack).LocalRotation.GetCardinalDir(),
                Is.EqualTo(Direction.East), "the flatpack should keep the part's facing");
        });

        await pair.CleanReturnAsync();
    }
}
