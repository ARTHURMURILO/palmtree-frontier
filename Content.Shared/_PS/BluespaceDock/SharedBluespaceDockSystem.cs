using Content.Shared.Containers.ItemSlots;
using Content.Shared._NF.Shipyard.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// Shared half of the bluespace dock: registers the console's ID card slot on both sides (the
/// client needs the slot for the item slot button, the server for handling it), mirroring how the
/// shipyard console does it, and owns the deed helper both dock consoles use.
/// </summary>
public sealed class SharedBluespaceDockSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BluespaceDockConsoleComponent, ComponentInit>(OnConsoleInit);
        SubscribeLocalEvent<BluespaceDockConsoleComponent, ComponentRemove>(OnConsoleRemove);
    }

    private void OnConsoleInit(EntityUid uid, BluespaceDockConsoleComponent component, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, BluespaceDockConsoleComponent.TargetIdCardSlotId, component.TargetIdSlot);
    }

    private void OnConsoleRemove(EntityUid uid, BluespaceDockConsoleComponent component, ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(uid, component.TargetIdSlot);
    }

    /// <summary>
    /// Palmtree: writes a shuttle deed onto an entity. <see cref="ShuttleDeedComponent"/> lists this
    /// system in its Access attribute, so the dock and the ship registrar can create deeds without
    /// the core component being opened up to every system.
    /// </summary>
    public void AssignDeed(EntityUid uid, EntityUid? shuttleUid, string? name, string? suffix, string? owner)
    {
        var deed = EnsureComp<ShuttleDeedComponent>(uid);
        deed.ShuttleUid = shuttleUid;
        deed.ShuttleName = name;
        deed.ShuttleNameSuffix = suffix;
        deed.ShuttleOwner = owner;
        Dirty(uid, deed);
    }
}
