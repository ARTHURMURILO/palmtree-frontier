using Content.Server._PS.Protean;
using Content.Shared._PS.Protean;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: Prototype-level Protean fold/unfold loop using the polymorph parked-map model.
/// </summary>
[TestFixture]
public sealed class ProteanTest
{
    [Test]
    public async Task SpeciesSpawnsWithSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var proteanSystem = entMan.System<ProteanSystem>();

            var body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            var backpack = entMan.SpawnEntity("ClothingBackpack", map.GridCoords);
            Assert.That(inventory.TryEquip(body, backpack, "back", silent: true, force: true), Is.True);
            Assert.That(entMan.HasComponent<ProteanComponent>(body), Is.True);
            Assert.That(entMan.HasComponent<Content.Shared.Nutrition.Components.HungerComponent>(body), Is.False);
            Assert.That(entMan.HasComponent<Content.Shared.Nutrition.Components.ThirstComponent>(body), Is.False);
            Assert.That(entMan.HasComponent<Content.Shared.Body.Components.BodyComponent>(body), Is.True);
            var bodySystem = entMan.System<Content.Shared.Body.Systems.SharedBodySystem>();
            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>(
                (body, entMan.GetComponent<Content.Shared.Body.Components.BodyComponent>(body)), out var refactories), Is.True);
            Assert.That(refactories, Is.Not.Empty);

            var suit = proteanSystem.EnsureSuit(body);
            Assert.That(suit, Is.Not.Null);
            var suitUid = suit!.Value;
            var protean = entMan.GetComponent<ProteanComponent>(body);
            Assert.That(protean.Suit, Is.EqualTo(suit));
            Assert.That(protean.FoldActionEntity, Is.Not.Null);
            Assert.That(inventory.TryGetSlotEntity(body, "back", out var back), Is.True);
            Assert.That(back, Is.EqualTo(suit));

            var suitComp = entMan.GetComponent<ProteanSuitComponent>(suitUid);
            Assert.That(inventory.TryUnequip(body, "back", silent: true, force: false), Is.False,
                "the integrated suit must not be removable by its Protean owner");

            entMan.EventBus.RaiseLocalEvent(body, new ProteanLockEvent { Performer = body });
            Assert.That(suitComp.Locked, Is.True);

            var other = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suitComp.AllowRemoval = true;
            Assert.That(inventory.TryUnequip(body, "back", silent: true, force: true), Is.True);
            suitComp.AllowRemoval = false;
            Assert.That(inventory.TryEquip(other, suitUid, "back", silent: true, force: true), Is.True);
            Assert.That(inventory.TryUnequip(other, "back", silent: true, force: false), Is.False,
                "a locked Protean suit must not be removable by another wearer");

            entMan.DeleteEntity(other);
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FoldAndUnfold()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var proteanSystem = entMan.System<ProteanSystem>();

            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suit = entMan.SpawnEntity("ModsuitControlProtean", map.GridCoords);

            var protean = entMan.AddComponent<ProteanComponent>(body);
            protean.Suit = suit;

            Assert.That(inventory.TryEquip(body, suit, "back", silent: true, force: true), Is.True);

            Assert.That(proteanSystem.TryFold(body, suit), Is.True, "Protean should fold into the suit");
            Assert.That(protean.Folded, Is.True);
            Assert.That(protean.Controller, Is.Not.Null);

            var controller = protean.Controller!.Value;
            Assert.That(entMan.HasComponent<ProteanControllerComponent>(controller), Is.True);
            Assert.That(entMan.GetComponent<ProteanControllerComponent>(controller).OriginalBody, Is.EqualTo(body));
            Assert.That(entMan.GetComponent<ProteanControllerComponent>(controller).Suit, Is.EqualTo(suit));
            Assert.That(entMan.GetComponent<TransformComponent>(controller).ParentUid, Is.EqualTo(suit),
                "the controller should follow the suit");

            Assert.That(proteanSystem.TryUnfold(controller), Is.True, "Protean should unfold from the suit");
            Assert.That(protean.Folded, Is.False);
            Assert.That(protean.Controller, Is.Null);
            Assert.That(inventory.TryGetSlotEntity(body, "back", out var back), Is.True);
            Assert.That(back, Is.EqualTo(suit), "the suit should be re-equipped after unfolding");

            entMan.DeleteEntity(body);
            entMan.DeleteEntity(suit);
        });

        await pair.CleanReturnAsync();
    }
}
