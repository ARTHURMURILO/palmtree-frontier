#nullable enable
using System.Threading.Tasks;
using Content.Shared._PS.Clothing;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: rings can be attached to gloves and must be the only thing that fits in the glove's
/// ring slot. Examining the gloves should mention the attached ring.
/// </summary>
[TestFixture]
public sealed class RingHolderTest
{
    [Test]
    public async Task OnlyRingsCanBeAttached()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var slots = entMan.System<ItemSlotsSystem>();
            var gloves = entMan.SpawnEntity("ClothingHandsGlovesColorBlack", MapCoordinates.Nullspace);
            var ring = entMan.SpawnEntity("GoldRing", MapCoordinates.Nullspace);
            var pen = entMan.SpawnEntity("Pen", MapCoordinates.Nullspace);

            var slotId = RingHolderComponent.GetSlotId(0);
            Assert.That(slots.TryGetSlot(gloves, slotId, out var slot), Is.True,
                "gloves have no ring slot");

            Assert.That(slots.CanInsert(gloves, ring, null, slot!, swap: false), Is.True,
                "gloves rejected a ring");
            Assert.That(slots.CanInsert(gloves, pen, null, slot!, swap: false), Is.False,
                "gloves accepted a non-ring item");

            Assert.That(slots.TryInsert(gloves, slotId, ring, null), Is.True,
                "could not insert a ring into gloves");
            Assert.That(slot!.Item, Is.EqualTo(ring), "ring was not stored in the glove slot");

            var examine = new ExaminedEvent(new FormattedMessage(), gloves, gloves, true, false);
            entMan.EventBus.RaiseLocalEvent(gloves, examine, true);
            Assert.That(examine.GetTotalMessage().ToString(), Does.Contain("gold ring"),
                "gloves do not mention the attached ring when examined");
        });

        await pair.CleanReturnAsync();
    }
}
