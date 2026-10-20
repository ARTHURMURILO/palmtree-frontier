using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using System.Linq;
using Content.Shared._PS.Modsuits;
using Content.Shared.Actions;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Modsuits;

/// <summary>
/// Server-authoritative MODsuit deployment, sealing, power and part lifecycle.
/// </summary>
public sealed class ModsuitSystem : SharedModsuitSystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModsuitControlComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ModsuitControlComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ModsuitControlComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ModsuitControlComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<ModsuitControlComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<ModsuitControlComponent, ModsuitToggleEvent>(OnToggle);
        SubscribeLocalEvent<ModsuitControlComponent, ModsuitSealEvent>(OnSeal);
        SubscribeLocalEvent<ModsuitControlComponent, ModsuitUiEvent>(OnUi);
        Subs.BuiEvents<ModsuitControlComponent>(ModsuitUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<ModsuitToggleMessage>(OnToggleMessage);
            subs.Event<ModsuitSealMessage>(OnSealMessage);
        });
        SubscribeLocalEvent<ModsuitControlComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnInit(Entity<ModsuitControlComponent> ent, ref ComponentInit args)
    {
        ent.Comp.PartsContainer = _container.EnsureContainer<Container>(ent, ModsuitControlComponent.PartsContainerId);
        ent.Comp.ModuleContainer = _container.EnsureContainer<Container>(ent, ModsuitControlComponent.ModuleContainerId);
        ent.Comp.OverslotContainer = _container.EnsureContainer<Container>(ent, ModsuitControlComponent.OverslotContainerId);
        ent.Comp.CoreContainer = _container.EnsureContainer<ContainerSlot>(ent, ModsuitControlComponent.CoreContainerId);
    }

    private void OnMapInit(Entity<ModsuitControlComponent> ent, ref MapInitEvent args)
    {
        if (!Prototypes.TryIndex(ent.Comp.Theme, out var theme))
        {
            Log.Error($"MODsuit {ToPrettyString(ent)} references unknown theme {ent.Comp.Theme}.");
            return;
        }

        EnsureParts(ent);
        EnsureCore(ent);

        _actions.AddAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction, ent.Owner);
        _actions.AddAction(ent.Owner, ref ent.Comp.SealActionEntity, ent.Comp.SealAction, ent.Owner);
        _actions.AddAction(ent.Owner, ref ent.Comp.UiActionEntity, ent.Comp.UiAction, ent.Owner);

        ApplyTheme(ent, theme);
        Dirty(ent.Owner, ent.Comp);
        UpdateUi(ent);
        var modulesEv = new ModsuitModulesInitEvent();
        RaiseLocalEvent(ent.Owner, ref modulesEv);
    }

    private void OnGetActions(Entity<ModsuitControlComponent> ent, ref GetItemActionsEvent args)
    {
        if ((args.SlotFlags & SlotFlags.BACK) == 0)
            return;

        if (ent.Comp.ToggleActionEntity is { } toggle)
            args.AddAction(toggle);

        if (ent.Comp.SealActionEntity is { } seal)
            args.AddAction(seal);

        if (ent.Comp.UiActionEntity is { } ui)
            args.AddAction(ui);
    }

    private void OnEquipped(Entity<ModsuitControlComponent> ent, ref GotEquippedEvent args)
    {
        if ((args.SlotFlags & SlotFlags.BACK) == 0)
            return;

        ent.Comp.Wearer = args.Equipee;
        Dirty(ent.Owner, ent.Comp);
    }

    private void OnUnequipped(Entity<ModsuitControlComponent> ent, ref GotUnequippedEvent args)
    {
        if (ent.Comp.Wearer != args.Equipee)
            return;

        Retract(ent, args.Equipee);
        ent.Comp.Wearer = null;
        Dirty(ent.Owner, ent.Comp);
    }

    private void OnToggle(Entity<ModsuitControlComponent> ent, ref ModsuitToggleEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Active)
            Retract(ent, args.Performer);
        else
            Deploy(ent, args.Performer);

        args.Handled = true;
    }

    private void OnSeal(Entity<ModsuitControlComponent> ent, ref ModsuitSealEvent args)
    {
        if (args.Handled)
            return;

        if (!ent.Comp.Active)
        {
            _popup.PopupEntity(Loc.GetString("modsuit-not-active"), ent.Owner, args.Performer);
            args.Handled = true;
            return;
        }

        SetSealed(ent, !ent.Comp.Sealed);
        args.Handled = true;
    }

    private void OnUi(Entity<ModsuitControlComponent> ent, ref ModsuitUiEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Wearer != args.Performer)
        {
            _popup.PopupEntity(Loc.GetString("modsuit-not-worn"), ent.Owner, args.Performer);
            args.Handled = true;
            return;
        }

        _ui.TryOpenUi(ent.Owner, ModsuitUiKey.Key, args.Performer);
        UpdateUi(ent);
        args.Handled = true;
    }

    private void OnUiOpened(EntityUid uid, ModsuitControlComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnToggleMessage(EntityUid uid, ModsuitControlComponent comp, ModsuitToggleMessage args)
    {
        if (comp.Wearer != args.Actor)
            return;

        var ent = (uid, comp);
        if (comp.Active)
            Retract(ent, args.Actor);
        else
            Deploy(ent, args.Actor);

        UpdateUi(ent);
    }

    private void OnSealMessage(EntityUid uid, ModsuitControlComponent comp, ModsuitSealMessage args)
    {
        if (comp.Wearer != args.Actor || !comp.Active)
            return;

        SetSealed((uid, comp), !comp.Sealed);
        UpdateUi((uid, comp));
    }



    private void OnTerminating(Entity<ModsuitControlComponent> ent, ref EntityTerminatingEvent args)
    {
        foreach (var part in ent.Comp.Parts.Values)
        {
            if (!TerminatingOrDeleted(part))
                QueueDel(part);
        }

        ent.Comp.Parts.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ModsuitControlComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Active)
                continue;

            if (!Prototypes.TryIndex(comp.Theme, out var theme))
                continue;

            if (!TryComp<ModsuitCoreComponent>(comp.Core, out var core))
            {
                Retract((uid, comp), comp.Wearer);
                continue;
            }

            var drain = theme.ChargeDrain * core.DrainMultiplier * frameTime;
            if (drain <= 0 || core.TryUseCharge(drain))
            {
                if (!core.Infinite)
                    Dirty(comp.Core.Value, core);
                continue;
            }

            _popup.PopupEntity(Loc.GetString("modsuit-no-power"), uid, comp.Wearer ?? uid);
            Retract((uid, comp), comp.Wearer);
        }
    }

    private void Deploy(Entity<ModsuitControlComponent> ent, EntityUid performer)
    {
        var comp = ent.Comp;
        if (comp.Active)
            return;

        var wearer = comp.Wearer;
        if (wearer == null || wearer != performer || Deleted(wearer.Value))
        {
            _popup.PopupEntity(Loc.GetString("modsuit-not-worn"), ent.Owner, performer);
            return;
        }

        if (!_inventory.TryGetSlotEntity(wearer.Value, "back", out var back) || back != ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString("modsuit-not-worn"), ent.Owner, performer);
            return;
        }

        if (!TryComp<ModsuitCoreComponent>(comp.Core, out var core) || core.GetCharge() <= 0)
        {
            _popup.PopupEntity(Loc.GetString("modsuit-no-power"), ent.Owner, performer);
            return;
        }

        var deployed = new List<EntityUid>();
        foreach (var (slot, part) in comp.Parts)
        {
            if (Deleted(part) || !comp.PartsContainer.Contains(part))
                continue;

            var slotId = GetSlotId(slot);
            if (_inventory.TryGetSlotEntity(wearer.Value, slotId, out var existing) && existing != part)
            {
                if (!_inventory.TryUnequip(wearer.Value, slotId, silent: true, force: true, reparent: false))
                {
                    CleanupFailedDeploy(ent, deployed);
                    return;
                }

                if (!_container.Insert(existing.Value, comp.OverslotContainer))
                    _transform.DropNextTo(existing.Value, wearer.Value);
            }

            _container.Remove(part, comp.PartsContainer, force: true, reparent: false);
            if (!_inventory.TryEquip(wearer.Value, part, slotId, silent: true, force: true))
            {
                _container.Insert(part, comp.PartsContainer, force: true);
                CleanupFailedDeploy(ent, deployed);
                return;
            }

            deployed.Add(part);
        }

        comp.Active = true;
        comp.Sealed = false;
        Dirty(ent.Owner, ent.Comp);

        if (Prototypes.TryIndex(ent.Comp.Theme, out var theme))
            ApplyTheme(ent, theme);

        _popup.PopupEntity(Loc.GetString("modsuit-deployed"), ent.Owner, performer);
        UpdateUi(ent);
    }

    private void CleanupFailedDeploy(Entity<ModsuitControlComponent> ent, List<EntityUid> deployed)
    {
        var wearer = ent.Comp.Wearer;
        foreach (var part in deployed)
        {
            if (wearer != null && TryComp<ModsuitPartComponent>(part, out var partComp))
                _inventory.TryUnequip(wearer.Value, GetSlotId(partComp.Slot), silent: true, force: true, reparent: false);

            _container.Insert(part, ent.Comp.PartsContainer, force: true);
        }

        ent.Comp.Active = false;
        Dirty(ent.Owner, ent.Comp);
    }

    private void Retract(Entity<ModsuitControlComponent> ent, EntityUid? wearer)
    {
        if (!ent.Comp.Active)
            return;

        SetSealed(ent, false, silent: true);

        if (wearer is { } wearerUid && !Deleted(wearerUid))
        {
            foreach (var (slot, part) in ent.Comp.Parts)
            {
                if (Deleted(part))
                    continue;

                _inventory.TryUnequip(wearerUid, GetSlotId(slot), silent: true, force: true, reparent: false);
                _container.Insert(part, ent.Comp.PartsContainer, force: true);
            }

            RestoreOverslot(ent, wearerUid);
            _popup.PopupEntity(Loc.GetString("modsuit-retracted"), ent.Owner, wearerUid);
        }

        ent.Comp.Active = false;
        ent.Comp.Sealed = false;
        Dirty(ent.Owner, ent.Comp);
        UpdateUi(ent);
    }

    private void RestoreOverslot(Entity<ModsuitControlComponent> ent, EntityUid wearer)
    {
        foreach (var item in ent.Comp.OverslotContainer.ContainedEntities.ToArray())
        {
            var slot = "head";
            if (TryComp<ClothingComponent>(item, out var clothing))
            {
                if ((clothing.Slots & SlotFlags.HEAD) != 0)
                    slot = "head";
                else if ((clothing.Slots & SlotFlags.OUTERCLOTHING) != 0)
                    slot = "outerClothing";
                else if ((clothing.Slots & SlotFlags.GLOVES) != 0)
                    slot = "gloves";
                else if ((clothing.Slots & SlotFlags.FEET) != 0)
                    slot = "shoes";
            }

            _container.Remove(item, ent.Comp.OverslotContainer, force: true, reparent: false);
            if (!_inventory.TryEquip(wearer, item, slot, silent: true, force: true))
                _transform.DropNextTo(item, wearer);
        }
    }

    private void SetSealed(Entity<ModsuitControlComponent> ent, bool isSealed, bool silent = false)
    {
        if (ent.Comp.Sealed == isSealed)
            return;

        ent.Comp.Sealed = isSealed;
        if (!Prototypes.TryIndex(ent.Comp.Theme, out var theme))
            return;

        foreach (var (slot, part) in ent.Comp.Parts)
        {
            if (Deleted(part))
                continue;

            if (TryComp<ModsuitPartComponent>(part, out var partComp))
            {
                partComp.Sealed = isSealed;
                Dirty(part, partComp);
            }

            if (isSealed)
            {
                var pressure = EnsureComp<PressureProtectionComponent>(part);
                pressure.HighPressureMultiplier = theme.HighPressureMultiplier;
                pressure.LowPressureMultiplier = theme.LowPressureMultiplier;

                var temperature = EnsureComp<TemperatureProtectionComponent>(part);
                temperature.HeatingCoefficient = theme.HeatingCoefficient;
                temperature.CoolingCoefficient = theme.CoolingCoefficient;
            }
            else
            {
                RemComp<PressureProtectionComponent>(part);
                RemComp<TemperatureProtectionComponent>(part);
            }

            ApplyPartVisuals(part, slot, theme, ent.Comp.Skin, isSealed);
        }

        Dirty(ent.Owner, ent.Comp);
        if (!silent)
        {
            var key = isSealed ? "modsuit-sealed" : "modsuit-unsealed";
            _popup.PopupEntity(Loc.GetString(key), ent.Owner, ent.Comp.Wearer ?? ent.Owner);
        }

        UpdateUi(ent);
    }

    public void UpdateUi(Entity<ModsuitControlComponent> ent)
    {
        var state = new ModsuitUiState
        {
            Active = ent.Comp.Active,
            Sealed = ent.Comp.Sealed,
            Open = ent.Comp.Open,
            Skin = ent.Comp.Skin,
        };

        if (Prototypes.TryIndex(ent.Comp.Theme, out var theme))
        {
            state.Theme = theme.Name;
            state.ComplexityMax = theme.ComplexityMax;
        }

        if (TryComp<ModsuitCoreComponent>(ent.Comp.Core, out var core))
        {
            state.Infinite = core.Infinite;
            state.Charge = core.Infinite ? 0f : core.GetCharge();
            state.MaxCharge = core.Infinite ? 0f : core.GetMaxCharge();
        }

        foreach (var module in ent.Comp.Modules)
        {
            if (!TryComp<ModsuitModuleComponent>(module, out var moduleComp))
                continue;

            state.Complexity += moduleComp.Complexity;
            state.Modules.Add(new ModsuitModuleEntry(
                Name(module),
                moduleComp.ModuleType,
                moduleComp.Complexity,
                moduleComp.Active));
        }

        foreach (var (slot, part) in ent.Comp.Parts)
        {
            if (!TryComp<ModsuitPartComponent>(part, out var partComp))
                continue;

            var equipped = false;
            if (ent.Comp.Wearer is { } wearer)
            {
                var slotId = GetSlotId(slot);
                equipped = _inventory.TryGetSlotEntity(wearer, slotId, out var equippedPart) && equippedPart == part;
            }

            state.Parts.Add(new ModsuitPartEntry(slot.ToString(), partComp.Sealed, equipped));
        }

        _ui.SetUiState(ent.Owner, ModsuitUiKey.Key, state);
    }

    private void EnsureParts(Entity<ModsuitControlComponent> ent)
    {
        foreach (var contained in ent.Comp.PartsContainer.ContainedEntities)
        {
            if (!TryComp<ModsuitPartComponent>(contained, out var containedPart))
                continue;

            ent.Comp.Parts.TryAdd(containedPart.Slot, contained);
            containedPart.Control = ent;
        }

        var prototypes = new Dictionary<ModsuitPartSlot, EntProtoId>
        {
            [ModsuitPartSlot.Helmet] = ent.Comp.HelmetPrototype,
            [ModsuitPartSlot.Chestplate] = ent.Comp.ChestplatePrototype,
            [ModsuitPartSlot.Gauntlets] = ent.Comp.GauntletsPrototype,
            [ModsuitPartSlot.Boots] = ent.Comp.BootsPrototype,
        };

        foreach (var (slot, proto) in prototypes)
        {
            if (ent.Comp.Parts.TryGetValue(slot, out var current) && !Deleted(current))
                continue;

            var part = Spawn(proto, Transform(ent.Owner).Coordinates);
            if (!TryComp<ModsuitPartComponent>(part, out var partComp))
            {
                Log.Error($"MODsuit part prototype {proto} is missing ModsuitPartComponent.");
                QueueDel(part);
                continue;
            }

            partComp.Control = ent;
            partComp.Slot = slot;
            var attached = EnsureComp<AttachedClothingComponent>(part);
            attached.AttachedUid = ent.Owner;
            Dirty(part, attached);
            _container.Insert(part, ent.Comp.PartsContainer, force: true);
            ent.Comp.Parts[slot] = part;
        }
    }

    private void EnsureCore(Entity<ModsuitControlComponent> ent)
    {
        if (ent.Comp.Core is { } current && !Deleted(current))
            return;

        if (ent.Comp.CoreContainer.ContainedEntity is { } existing)
        {
            ent.Comp.Core = existing;
            return;
        }

        var core = Spawn(ent.Comp.CorePrototype, Transform(ent.Owner).Coordinates);
        if (!HasComp<ModsuitCoreComponent>(core))
        {
            Log.Error($"MODsuit core prototype {ent.Comp.CorePrototype} is missing ModsuitCoreComponent.");
            QueueDel(core);
            return;
        }

        _container.Insert(core, ent.Comp.CoreContainer, force: true);
        ent.Comp.Core = core;
    }

    public void SetTheme(
        Entity<ModsuitControlComponent> ent,
        ProtoId<ModsuitThemePrototype> themeId,
        string? skin = null)
    {
        if (!Prototypes.TryIndex(themeId, out var theme))
            return;

        ent.Comp.Theme = themeId;
        ent.Comp.Skin = skin ?? theme.DefaultSkin;
        ApplyTheme(ent, theme);
        Dirty(ent.Owner, ent.Comp);
        UpdateUi(ent);
    }

    private void ApplyTheme(Entity<ModsuitControlComponent> ent, ModsuitThemePrototype theme)
    {
        foreach (var (slot, part) in ent.Comp.Parts)
        {
            if (Deleted(part))
                continue;

            ApplyArmor(part, theme);
            ApplyPartVisuals(part, slot, theme, ent.Comp.Skin, ent.Comp.Sealed);
        }
    }

    private static string GetSlotId(ModsuitPartSlot slot)
    {
        return slot switch
        {
            ModsuitPartSlot.Helmet => "head",
            ModsuitPartSlot.Chestplate => "outerClothing",
            ModsuitPartSlot.Gauntlets => "gloves",
            ModsuitPartSlot.Boots => "shoes",
            _ => "head",
        };
    }


}
