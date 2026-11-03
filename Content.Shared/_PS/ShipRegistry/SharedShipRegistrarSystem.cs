using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// Registers the registrar's two ID card slots on both sides, like the shipyard console does for its
/// single slot.
/// </summary>
public sealed class SharedShipRegistrarSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipRegistrarConsoleComponent, ComponentInit>(OnConsoleInit);
        SubscribeLocalEvent<ShipRegistrarConsoleComponent, ComponentRemove>(OnConsoleRemove);
    }

    private void OnConsoleInit(EntityUid uid, ShipRegistrarConsoleComponent component, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, ShipRegistrarConsoleComponent.PrivilegedIdCardSlotId, component.PrivilegedIdSlot);
        _itemSlots.AddItemSlot(uid, ShipRegistrarConsoleComponent.TargetIdCardSlotId, component.TargetIdSlot);
    }

    private void OnConsoleRemove(EntityUid uid, ShipRegistrarConsoleComponent component, ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(uid, component.PrivilegedIdSlot);
        _itemSlots.RemoveItemSlot(uid, component.TargetIdSlot);
    }
}
