using Content.Shared._PS.Clothing;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Clothing;

/// <summary>
///     Handles server-side setup and feedback for rings attached to gloves.
/// </summary>
public sealed class ServerRingHolderSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> RingTag = "Ring";

    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RingHolderComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RingHolderComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnExamined(Entity<RingHolderComponent> ent, ref ExaminedEvent args)
    {
        if (!TryComp<ItemSlotsComponent>(ent.Owner, out var itemSlots))
            return;

        for (var i = 0; i < ent.Comp.MaxRings; i++)
        {
            if (!_itemSlots.TryGetSlot(ent.Owner, RingHolderComponent.GetSlotId(i), out var slot, itemSlots) ||
                slot.Item is not { } ring)
            {
                continue;
            }

            args.PushMarkup(Loc.GetString("gloves-ring-examine", ("ring", ring)));
        }
    }

    private void OnInteractUsing(Entity<RingHolderComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tags.HasTag(args.Used, RingTag))
            return;

        if (!TryComp<ItemSlotsComponent>(ent.Owner, out var itemSlots))
            return;

        var anyFilled = false;
        var anyFree = false;

        // If any configured ring slot is free, let ItemSlotsSystem handle the insertion. Only show
        // the "full" popup once every ring slot is occupied.
        for (var i = 0; i < ent.Comp.MaxRings; i++)
        {
            if (!_itemSlots.TryGetSlot(ent.Owner, RingHolderComponent.GetSlotId(i), out var slot, itemSlots))
                continue;

            if (slot.HasItem)
                anyFilled = true;
            else
                anyFree = true;
        }

        if (!anyFilled || anyFree)
            return;

        _popup.PopupEntity(Loc.GetString("gloves-ring-slot-full"), ent, args.User);
        args.Handled = true;
    }
}
