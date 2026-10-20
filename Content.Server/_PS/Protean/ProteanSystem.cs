using Content.Server.Polymorph.Systems;
using Content.Shared._PS.Modsuits;
using Content.Server._PS.Modsuits;
using Content.Shared._PS.Protean;
using Content.Shared.Actions;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Polymorph;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._PS.Protean;

/// <summary>
/// Minimal Protean fold/unfold runtime. The original body is parked by the polymorph system; the player
/// controls an invisible controller attached to the normal MODsuit item.
/// </summary>
public sealed class ProteanSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProteanComponent, ComponentInit>(OnProteanInit);
        SubscribeLocalEvent<ProteanComponent, PolymorphedEvent>(OnPolymorphed);
        SubscribeLocalEvent<ProteanComponent, ProteanFoldEvent>(OnFold);
        SubscribeLocalEvent<ProteanControllerComponent, ProteanUnfoldEvent>(OnUnfold);
        SubscribeLocalEvent<ProteanComponent, ProteanLockEvent>(OnLock);
        SubscribeLocalEvent<ProteanControllerComponent, ProteanLockEvent>(OnControllerLock);
        SubscribeLocalEvent<ProteanSuitComponent, BeingUnequippedAttemptEvent>(OnSuitUnequipAttempt);
        SubscribeLocalEvent<ProteanSuitComponent, ModsuitToggleEvent>(OnSuitToggle, before: new[] { typeof(ModsuitSystem) });
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnProteanInit(Entity<ProteanComponent> ent, ref ComponentInit args)
    {
        RemComp<HungerComponent>(ent);
        RemComp<ThirstComponent>(ent);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (HasComp<ProteanComponent>(args.Mob))
            EnsureSuit(args.Mob);
    }

    private void OnFold(Entity<ProteanComponent> ent, ref ProteanFoldEvent args)
    {
        if (args.Handled)
            return;

        var suit = ent.Comp.Suit;
        if (suit == null || Deleted(suit.Value))
            suit = EnsureSuit(ent);

        if (suit is { } validSuit)
            args.Handled = TryFold(ent, validSuit);
    }

    private void OnUnfold(Entity<ProteanControllerComponent> ent, ref ProteanUnfoldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryUnfold(ent);
    }

    private void OnLock(Entity<ProteanComponent> ent, ref ProteanLockEvent args)
    {
        if (args.Handled || ent.Comp.Suit is not { } suit)
            return;

        ToggleLock(ent, suit);
        args.Handled = true;
    }

    private void OnControllerLock(Entity<ProteanControllerComponent> ent, ref ProteanLockEvent args)
    {
        if (args.Handled || ent.Comp.Suit is not { } suit)
            return;

        ToggleLock(ent, suit);
        args.Handled = true;
    }

    private void ToggleLock(EntityUid owner, EntityUid suit)
    {
        if (!TryComp<ProteanSuitComponent>(suit, out var suitComp))
            return;

        suitComp.Locked = !suitComp.Locked;
        _popup.PopupEntity(Loc.GetString(suitComp.Locked ? "protean-locked" : "protean-unlocked"), suit, owner);
    }

    private void OnSuitUnequipAttempt(Entity<ProteanSuitComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if (ent.Comp.Owner == args.UnEquipTarget && !ent.Comp.AllowRemoval)
        {
            args.Cancel();
            args.Reason = "protean-suit-integrated";
            return;
        }

        if (ent.Comp.Locked && args.UnEquipTarget != ent.Comp.Owner)
        {
            args.Cancel();
            args.Reason = "protean-suit-locked";
        }
    }

    private void OnSuitToggle(Entity<ProteanSuitComponent> ent, ref ModsuitToggleEvent args)
    {
        if (!ent.Comp.Locked || ent.Comp.Owner == args.Performer)
            return;

        _popup.PopupEntity(Loc.GetString("protean-suit-locked"), ent.Owner, args.Performer);
        args.Handled = true;
    }

    public EntityUid? EnsureSuit(EntityUid body)
    {
        if (!TryComp<ProteanComponent>(body, out var protean))
            return null;

        if (protean.Suit is { } existing && !Deleted(existing))
        {
            EquipSuit(body, existing);
            _actions.AddAction(body, ref protean.FoldActionEntity, "ActionProteanFold", body);
            _actions.AddAction(body, ref protean.LockActionEntity, "ActionProteanLock", body);
            return existing;
        }

        var suit = Spawn("ModsuitControlProtean", Transform(body).Coordinates);
        if (!TryComp<ProteanSuitComponent>(suit, out var suitComp))
            suitComp = EnsureComp<ProteanSuitComponent>(suit);

        suitComp.Owner = body;
        protean.Suit = suit;

        if (!EquipSuit(body, suit))
        {
            _popup.PopupEntity(Loc.GetString("protean-suit-stuck"), suit, body);
            return suit;
        }

        _actions.AddAction(body, ref protean.FoldActionEntity, "ActionProteanFold", body);
        _actions.AddAction(body, ref protean.LockActionEntity, "ActionProteanLock", body);
        return suit;
    }

    private bool EquipSuit(EntityUid body, EntityUid suit)
    {
        if (_inventory.TryGetSlotEntity(body, "back", out var back))
        {
            if (back == suit)
                return true;

            // Spawn equipment (backpacks) would otherwise leave the suit on the floor and block the
            // back slot. Drop the existing item first, matching the source's forced suit equip.
            _inventory.TryUnequip(body, "back", silent: true, force: true);
        }

        return _inventory.TryEquip(body, suit, "back", silent: true, force: true);
    }

    public bool TryFold(EntityUid body, EntityUid suit)
    {
        if (!TryComp<ProteanComponent>(body, out var protean) || protean.Folded)
            return false;

        if (!TryComp<ProteanSuitComponent>(suit, out _))
            EnsureComp<ProteanSuitComponent>(suit);

        if (!TryComp<ModsuitControlComponent>(suit, out var suitControl) || suitControl.Active)
            return false;

        if (_inventory.TryGetSlotEntity(body, "back", out var back) && back == suit)
        {
            if (!TryComp<ProteanSuitComponent>(suit, out var suitComp))
                suitComp = EnsureComp<ProteanSuitComponent>(suit);

            suitComp.AllowRemoval = true;
            var unequipped = _inventory.TryUnequip(body, "back", silent: true, force: true);
            suitComp.AllowRemoval = false;
            if (!unequipped)
                return false;
        }
        else if (!_container.IsEntityInContainer(suit))
        {
            return false;
        }

        if (_container.TryGetContainingContainer((suit, null, null), out var container))
            _container.Remove(suit, container, force: true);

        _transform.SetCoordinates(suit, Transform(body).Coordinates);
        _transform.AttachToGridOrMap(suit);

        protean.PendingSuit = suit;
        var controller = _polymorph.PolymorphEntity(body, "ProteanSuitForm");
        if (controller == null)
        {
            protean.PendingSuit = null;
            _inventory.TryEquip(body, suit, "back", silent: true, force: true);
            return false;
        }

        return true;
    }

    public bool TryUnfold(EntityUid controller)
    {
        if (!TryComp<ProteanControllerComponent>(controller, out var controllerComp)
            || controllerComp.Suit is not { } suit
            || controllerComp.OriginalBody is not { } body)
            return false;

        if (!TryComp<ModsuitControlComponent>(suit, out var suitControl) || suitControl.Active)
            return false;

        if (suitControl.Wearer is { } wearer)
            _inventory.TryUnequip(wearer, "back", silent: true, force: true);

        if (_container.TryGetContainingContainer((suit, null, null), out var container))
            _container.Remove(suit, container, force: true);

        _transform.SetCoordinates(controller, Transform(suit).Coordinates);
        _transform.AttachToGridOrMap(controller);

        var restored = _polymorph.Revert(controller);
        if (restored == null)
            return false;

        _inventory.TryEquip(restored.Value, suit, "back", silent: true, force: true);
        if (TryComp<ProteanComponent>(restored.Value, out var protean))
        {
            protean.Folded = false;
            protean.Controller = null;
        }

        var suitComp = EnsureComp<ProteanSuitComponent>(suit);
        suitComp.Controller = null;
        return true;
    }

    private void OnPolymorphed(Entity<ProteanComponent> ent, ref PolymorphedEvent args)
    {
        if (args.IsRevert || ent.Comp.PendingSuit is not { } suit)
            return;

        var controller = args.NewEntity;
        ent.Comp.PendingSuit = null;
        ent.Comp.Folded = true;
        ent.Comp.Controller = controller;
        ent.Comp.Suit = suit;

        var controllerComp = EnsureComp<ProteanControllerComponent>(controller);
        controllerComp.OriginalBody = ent.Owner;
        controllerComp.Suit = suit;

        var suitComp = EnsureComp<ProteanSuitComponent>(suit);
        suitComp.Owner = ent.Owner;
        suitComp.Controller = controller;

        _transform.SetParent(controller, suit);
        RemComp<InputMoverComponent>(controller);
        RemComp<MobMoverComponent>(controller);
        RemComp<MovementSpeedModifierComponent>(controller);

        _actions.AddAction(controller, ref controllerComp.UnfoldActionEntity, "ActionProteanUnfold", controller);
        _actions.AddAction(controller, ref controllerComp.LockActionEntity, "ActionProteanLock", controller);
        _popup.PopupEntity(Loc.GetString("protean-folded"), suit, ent.Owner);
    }
}
