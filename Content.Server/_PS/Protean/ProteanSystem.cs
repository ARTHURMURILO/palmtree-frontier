using System.Linq;
using Content.Server.Polymorph.Systems;
using Content.Server._PS.Modsuits;
using Content.Shared._PS.Modsuits;
using Content.Shared._PS.Protean;
using Content.Server.Administration.Logs;
using Content.Shared.Actions;
using Content.Shared.Bed.Sleep;
using Content.Shared.Database;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Forensics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Stacks;
using Content.Shared.StatusEffectNew;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

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
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly ModsuitSystem _modsuits = default!;
    [Dependency] private readonly ModsuitModuleSystem _modules = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProteanComponent, ComponentInit>(OnProteanInit);
        SubscribeLocalEvent<ProteanComponent, ComponentShutdown>(OnProteanShutdown);
        SubscribeLocalEvent<ProteanComponent, PolymorphedEvent>(OnPolymorphed);
        SubscribeLocalEvent<ProteanComponent, ProteanFoldEvent>(OnFold);
        SubscribeLocalEvent<ProteanControllerComponent, ProteanUnfoldEvent>(OnUnfold);
        SubscribeLocalEvent<ProteanComponent, ProteanLockEvent>(OnLock);
        SubscribeLocalEvent<ProteanControllerComponent, ProteanLockEvent>(OnControllerLock);
        SubscribeLocalEvent<ProteanComponent, ProteanLowPowerEvent>(OnLowPower);
        SubscribeLocalEvent<ProteanComponent, ProteanHealEvent>(OnHeal);
        SubscribeLocalEvent<ProteanControllerComponent, ProteanHealEvent>(OnControllerHeal);
        SubscribeLocalEvent<ProteanSuitComponent, BeingUnequippedAttemptEvent>(OnSuitUnequipAttempt);
        SubscribeLocalEvent<ProteanSuitComponent, ModsuitToggleEvent>(OnSuitToggle, before: new[] { typeof(ModsuitSystem) });
        SubscribeLocalEvent<ProteanSuitComponent, ComponentInit>(OnSuitInit);
        SubscribeLocalEvent<ProteanSuitComponent, InteractUsingEvent>(OnSuitInteractUsing);
        SubscribeLocalEvent<ProteanSuitComponent, ProteanAssimilationDoAfterEvent>(OnAssimilationDoAfter);
        SubscribeLocalEvent<ProteanSuitComponent, ProteanRepairDoAfterEvent>(OnRepairDoAfter);
        SubscribeLocalEvent<ProteanSuitComponent, ProteanMemoryResetDoAfterEvent>(OnMemoryResetDoAfter);
        SubscribeLocalEvent<ProteanSuitComponent, EntityTerminatingEvent>(OnSuitTerminating);
        SubscribeLocalEvent<ProteanComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ProteanComponent, InteractUsingEvent>(OnProteanInteractUsing);
        SubscribeLocalEvent<OrganComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnProteanInit(Entity<ProteanComponent> ent, ref ComponentInit args)
    {
        RemComp<HungerComponent>(ent);
        RemComp<ThirstComponent>(ent);
        RemComp<DnaComponent>(ent);
    }

    /// <summary>
    /// If the Protean component is removed (species change, admin cleanup, body deletion), detach the
    /// integrated suit so it does not keep stale ownership or removal protection.
    /// </summary>
    private void OnProteanShutdown(Entity<ProteanComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Suit is { } suit
            && !TerminatingOrDeleted(suit)
            && TryComp<ProteanSuitComponent>(suit, out var suitComp))
        {
            suitComp.Owner = null;
            suitComp.Controller = null;
            suitComp.Locked = false;
            suitComp.AllowRemoval = true;
            RemComp<ProteanSuitComponent>(suit);
        }

        ent.Comp.Suit = null;
        ent.Comp.Controller = null;
    }

    private void OnProteanInteractUsing(Entity<ProteanComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled
            || ent.Comp.Folded
            || !_tags.HasTag(args.Used, "Metal")
            || !_tags.HasTag(args.Used, "Sheet")
            || !TryGetOrgan<ProteanRefactoryComponent>(ent.Owner, out var refactory))
            return;

        if (refactory.Comp1.Metal >= refactory.Comp1.MaxMetal)
        {
            _popup.PopupEntity(Loc.GetString("protean-metal-full"), ent.Owner, args.User);
            args.Handled = true;
            return;
        }

        refactory.Comp1.Metal = Math.Min(refactory.Comp1.MaxMetal, refactory.Comp1.Metal + 1f);
        Dirty(refactory.Owner, refactory.Comp1);

        if (TryComp<StackComponent>(args.Used, out var stack))
            _stacks.SetCount(args.Used, stack.Count - 1, stack);
        else
            QueueDel(args.Used);

        _popup.PopupEntity(Loc.GetString("protean-metal-eaten"), ent.Owner, args.User);
        args.Handled = true;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (HasComp<ProteanComponent>(args.Mob))
            EnsureSuit(args.Mob);
    }

    /// <summary>
    /// Proteans reject non-nanite organs in their core slots. The source waits one second before
    /// ejecting the offending organ; keep that delay so the insertion animation/state settles first.
    /// </summary>
    private void OnOrganAdded(Entity<OrganComponent> ent, ref OrganAddedToBodyEvent args)
    {
        if (!HasComp<ProteanComponent>(args.Body)
            || HasComp<ProteanOrganComponent>(ent.Owner)
            || _body.GetParentPartAndSlotOrNull(ent.Owner) is not { } parent
            || parent.Slot is not ("brain" or "heart" or "stomach" or "eyes"))
            return;

        var body = args.Body;
        Timer.Spawn(TimeSpan.FromSeconds(1), () =>
        {
            if (Deleted(body)
                || Deleted(ent.Owner)
                || !TryComp<OrganComponent>(ent.Owner, out var organ)
                || organ.Body != body)
                return;

            _body.RemoveOrgan(ent.Owner, organ);
            _transform.DropNextTo(ent.Owner, body);
            _popup.PopupEntity(Loc.GetString("protean-organ-rejected"), body, body);
        });
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

    private void OnLowPower(Entity<ProteanComponent> ent, ref ProteanLowPowerEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryToggleLowPower(ent.Owner, ent.Owner);
    }

    public bool TryToggleLowPower(EntityUid body, EntityUid performer)
    {
        if (!TryComp<ProteanComponent>(body, out var protean))
            return false;

        if (protean.Folded)
        {
            _popup.PopupEntity(Loc.GetString("protean-low-power-folded"), body, performer);
            return true;
        }

        if (!TryGetOrgan<ProteanRefactoryComponent>(body, out _))
        {
            _popup.PopupEntity(Loc.GetString("protean-low-power-no-refactory"), body, performer);
            return true;
        }

        if (_statusEffects.HasStatusEffect(body, "ProteanLowPowerStatusEffect"))
            _statusEffects.TryRemoveStatusEffect(body, "ProteanLowPowerStatusEffect");
        else
        {
            _statusEffects.TryAddStatusEffectDuration(body, "ProteanLowPowerStatusEffect", TimeSpan.FromDays(1));
            if (protean.Suit is { } suit && TryComp<ModsuitControlComponent>(suit, out var control) && control.Active)
                _modsuits.ForceRetract((suit, control));
        }

        RaiseSuitChanged(protean.Suit ?? body);
        return true;
    }

    private void OnHeal(Entity<ProteanComponent> ent, ref ProteanHealEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryHeal(ent.Owner, ent.Owner);
    }

    private void OnControllerHeal(Entity<ProteanControllerComponent> ent, ref ProteanHealEvent args)
    {
        if (args.Handled || ent.Comp.OriginalBody is not { } body)
            return;

        args.Handled = TryHeal(body, ent.Owner);
    }

    public bool TryHeal(EntityUid body, EntityUid performer)
    {
        if (!TryComp<ProteanComponent>(body, out var protean) || !protean.Folded)
        {
            _popup.PopupEntity(Loc.GetString("protean-heal-outside"), body, performer);
            return false;
        }

        if (!TryGetOrgan<ProteanRefactoryComponent>(body, out var refactory)
            || refactory.Comp1.Metal < 6f)
        {
            _popup.PopupEntity(Loc.GetString("protean-heal-no-metal"), body, performer);
            return false;
        }

        refactory.Comp1.Metal = Math.Max(0f, refactory.Comp1.Metal - 6f);
        Dirty(refactory.Owner, refactory.Comp1);

        if (TryComp<DamageableComponent>(body, out var damageable))
            _damageable.SetDamage(body, damageable, new DamageSpecifier());

        if (TryComp<BodyComponent>(body, out var bodyComp))
            RegrowBody(body, bodyComp);

        RaiseSuitChanged(protean.Suit ?? body);
        _popup.PopupEntity(Loc.GetString("protean-healed"), body, performer);
        return true;
    }

    /// <summary>
    /// Restores missing limbs and the non-critical synthetic organs from the body prototype.
    /// Critical organs (brain, heart, stomach) and the refactory are deliberately left alone; losing those
    /// requires the critical withdrawal/refactory repair path instead of a field repair.
    /// </summary>
    private void RegrowBody(EntityUid body, BodyComponent bodyComp)
    {
        if (bodyComp.Prototype is not { } prototypeId
            || !_prototypes.TryIndex(prototypeId, out var prototype))
            return;

        var rootSlotId = prototype.Root;
        var rootSlot = prototype.Slots[rootSlotId];
        EntityUid? root = bodyComp.RootContainer.ContainedEntity;

        if (root == null && rootSlot.Part is { } rootPartProto)
        {
            var newRoot = Spawn(rootPartProto, Transform(body).Coordinates);
            if (_container.Insert(newRoot, bodyComp.RootContainer, force: true))
                root = newRoot;
            else
                QueueDel(newRoot);
        }

        if (root == null)
            return;

        var entities = new Dictionary<string, EntityUid> { [rootSlotId] = root.Value };
        var seen = new HashSet<string> { rootSlotId };
        var frontier = new Queue<string>();
        frontier.Enqueue(rootSlotId);

        while (frontier.TryDequeue(out var parentSlotId))
        {
            if (!entities.TryGetValue(parentSlotId, out var parent)
                || !TryComp<BodyPartComponent>(parent, out var parentPart))
                continue;

            var parentProto = prototype.Slots[parentSlotId];
            foreach (var connection in parentProto.Connections)
            {
                if (!seen.Add(connection))
                    continue;

                if (!prototype.Slots.TryGetValue(connection, out var childSlot))
                    continue;

                EntityUid child;
                if (_container.TryGetContainer(parent, SharedBodySystem.GetPartSlotContainerId(connection), out var slotContainer)
                    && slotContainer.ContainedEntities.Count > 0
                    && TryComp<BodyPartComponent>(slotContainer.ContainedEntities[0], out _))
                {
                    child = slotContainer.ContainedEntities[0];
                }
                else if (childSlot.Part is { } childPartProto)
                {
                    child = Spawn(childPartProto, Transform(body).Coordinates);
                    _body.TryCreatePartSlot(parent, connection,
                        Comp<BodyPartComponent>(child).PartType, out _, parentPart);

                    if (!_body.AttachPart(parent, connection, child, parentPart))
                    {
                        QueueDel(child);
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                entities[connection] = child;
                frontier.Enqueue(connection);

                if (TryComp<BodyPartComponent>(child, out var childPart))
                    RegrowOrgans(child, childPart, childSlot);
            }
        }
    }

    private void RegrowOrgans(EntityUid part, BodyPartComponent partComp, BodyPrototypeSlot slot)
    {
        foreach (var (organSlotId, organProtoId) in slot.Organs)
        {
            // These are the critical organs. They are replaced through the refactory repair flow, not
            // a normal field repair.
            if (organSlotId is "brain" or "heart" or "stomach")
                continue;

            if (!_body.CanInsertOrgan(part, organSlotId, partComp))
                _body.TryCreateOrganSlot(part, organSlotId, out _, partComp);

            var containerId = SharedBodySystem.GetOrganContainerId(organSlotId);
            if (!_container.TryGetContainer(part, containerId, out var container))
                continue;

            if (container.ContainedEntities.Count > 0)
                continue;

            var organ = Spawn(organProtoId, Transform(part).Coordinates);
            if (!_container.Insert(organ, container, force: true))
                QueueDel(organ);
        }
    }

    public void ToggleLock(EntityUid owner, EntityUid suit)
    {
        if (!TryComp<ProteanSuitComponent>(suit, out var suitComp))
            return;

        suitComp.Locked = !suitComp.Locked;
        RaiseSuitChanged(suit);
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
        if (ent.Comp.Owner is { } owner
            && _statusEffects.HasStatusEffect(owner, "ProteanLowPowerStatusEffect"))
        {
            _popup.PopupEntity(Loc.GetString("protean-low-power-suit"), ent.Owner, args.Performer);
            args.Handled = true;
            return;
        }

        if (!ent.Comp.Locked || ent.Comp.Owner == args.Performer)
            return;

        _popup.PopupEntity(Loc.GetString("protean-suit-locked"), ent.Owner, args.Performer);
        args.Handled = true;
    }

    private void OnSuitInit(Entity<ProteanSuitComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, ProteanSuitContainer.StoredSuit);
    }

    private void OnMobStateChanged(Entity<ProteanComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive || ent.Comp.Folded)
            return;

        if (!TryGetOrgan<ProteanBrainComponent>(ent.Owner, out var brain) || brain.Comp1.Dead)
            return;

        BeginCriticalFold(ent, brain);
    }

    private void BeginCriticalFold(
        Entity<ProteanComponent> ent,
        Entity<ProteanBrainComponent, OrganComponent> brain)
    {
        brain.Comp1.Dead = true;
        Dirty(brain.Owner, brain.Comp1);

        // The refactory is destroyed by the withdrawal and must be replaced by a new organ.
        if (TryGetOrgan<ProteanRefactoryComponent>(ent.Owner, out var refactory))
        {
            _body.RemoveOrgan(refactory.Owner);
            QueueDel(refactory.Owner);
        }

        // Leave an alive body parked on the paused map; the brain flag controls unfolding/repair.
        if (TryComp<DamageableComponent>(ent.Owner, out var damageable))
            _damageable.SetDamage(ent.Owner, damageable, new DamageSpecifier());

        if (TryComp<MobStateComponent>(ent.Owner, out var mobState))
            _mobState.ChangeMobState(ent.Owner, MobState.Alive, mobState);

        var suit = ent.Comp.Suit;
        if (suit == null || Deleted(suit.Value))
            suit = EnsureSuit(ent.Owner);

        if (suit is not { } validSuit || !TryComp<ModsuitControlComponent>(validSuit, out var control))
            return;

        if (control.Active)
            _modsuits.ForceRetract((validSuit, control));

        control.Open = true;
        Dirty(validSuit, control);

        TryFold(ent, validSuit);
        RaiseSuitChanged(validSuit);
    }

    private void OnSuitInteractUsing(Entity<ProteanSuitComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Owner is not { } body)
            return;

        if (!TryComp<ModsuitControlComponent>(ent, out var control) || control.Active)
            return;

        if (_tags.HasTag(args.Used, "Pen"))
        {
            if (control.Open
                && TryGetOrgan<ProteanBrainComponent>(body, out var memoryBrain)
                && memoryBrain.Comp1.Dead)
            {
                var reset = new DoAfterArgs(
                    EntityManager,
                    args.User,
                    TimeSpan.FromSeconds(10),
                    new ProteanMemoryResetDoAfterEvent(),
                    ent.Owner,
                    target: ent.Owner,
                    used: args.Used)
                {
                    NeedHand = true,
                    BreakOnMove = true,
                    BreakOnDamage = true,
                };
                args.Handled = _doAfter.TryStartDoAfter(reset);
            }

            return;
        }

        if (TryComp<ProteanRefactoryComponent>(args.Used, out _))
        {
            if (!control.Open
                || !TryGetOrgan<ProteanBrainComponent>(body, out var brain)
                || !brain.Comp1.Dead)
                return;

            var repair = new DoAfterArgs(
                EntityManager,
                args.User,
                TimeSpan.FromSeconds(10),
                new ProteanRepairDoAfterEvent(),
                ent.Owner,
                target: ent.Owner,
                used: args.Used)
            {
                NeedHand = true,
                BreakOnMove = true,
                BreakOnDamage = true,
            };

            args.Handled = _doAfter.TryStartDoAfter(repair);
            return;
        }

        if (!TryComp<ModsuitControlComponent>(args.Used, out var targetControl))
        {
            if (TryAssimilatePlating(ent, args.Used, args.User))
                args.Handled = true;
            return;
        }

        if (ent.Comp.Owner != args.User
            || ent.Comp.StoredSuit != null
            || targetControl.Active
            || HasComp<ProteanSuitComponent>(args.Used))
            return;

        var assimilation = new DoAfterArgs(
            EntityManager,
            args.User,
            TimeSpan.FromSeconds(4),
            new ProteanAssimilationDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: args.Used)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };

        args.Handled = _doAfter.TryStartDoAfter(assimilation);
    }

    private void OnAssimilationDoAfter(Entity<ProteanSuitComponent> ent, ref ProteanAssimilationDoAfterEvent args)
    {
        if (args.Cancelled
            || args.Used is not { } target
            || !TryAssimilateSuit(ent, target, args.User))
            return;
    }

    public bool TryAssimilatePlating(Entity<ProteanSuitComponent> ent, EntityUid plating, EntityUid user)
    {
        if (ent.Comp.Owner != user
            || !TryComp<ModsuitPlatingComponent>(plating, out var platingComp)
            || !TryComp<ModsuitControlComponent>(ent, out var control)
            || control.Active)
            return false;

        var platingName = Name(plating);
        _modsuits.SetTheme((ent.Owner, control), platingComp.Theme);
        RaiseSuitChanged(ent);
        _popup.PopupEntity(Loc.GetString("protean-plating-assimilated", ("plating", platingName)), ent.Owner, user);
        QueueDel(plating);
        return true;
    }

    public bool TryAssimilateSuit(Entity<ProteanSuitComponent> ent, EntityUid target, EntityUid user)
    {
        if (ent.Comp.Owner != user
            || ent.Comp.StoredSuit != null
            || !TryComp<ModsuitControlComponent>(ent, out var control)
            || !TryComp<ModsuitControlComponent>(target, out var targetControl)
            || targetControl.Active
            || HasComp<ProteanSuitComponent>(target))
            return false;

        var container = _container.EnsureContainer<ContainerSlot>(ent, ProteanSuitContainer.StoredSuit);
        if (!_container.Insert(target, container, force: true))
            return false;

        ent.Comp.StoredSuit = target;
        ent.Comp.StoredTheme = control.Theme;
        ent.Comp.StoredSkin = control.Skin;

        _modsuits.SetTheme((ent, control), targetControl.Theme, targetControl.Skin);

        foreach (var module in targetControl.Modules.ToArray())
        {
            if (HasComp<ModsuitStorageModuleComponent>(module))
                continue;

            targetControl.Modules.Remove(module);
            if (_modules.TryInstallModule((ent.Owner, control), module, silent: true, user: user))
                ent.Comp.AssimilatedModules.Add(module);
            else
                targetControl.Modules.Add(module);
        }

        RaiseSuitChanged(ent);
        _popup.PopupEntity(Loc.GetString("protean-assimilated", ("suit", Name(target))), ent.Owner, user);
        return true;
    }

    private void OnRepairDoAfter(Entity<ProteanSuitComponent> ent, ref ProteanRepairDoAfterEvent args)
    {
        if (args.Cancelled
            || args.Used is not { } refactory
            || !TryRepair(ent, refactory, args.User))
            return;
    }

    private void OnMemoryResetDoAfter(Entity<ProteanSuitComponent> ent, ref ProteanMemoryResetDoAfterEvent args)
    {
        if (args.Cancelled || ent.Comp.Owner is not { } body)
            return;

        if (!TryGetOrgan<ProteanBrainComponent>(body, out var brain) || !brain.Comp1.Dead)
            return;

        _statusEffects.TryAddStatusEffectDuration(body, SleepingSystem.StatusEffectForcedSleeping, TimeSpan.FromMinutes(1));
        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(args.User)} reset the memory of {ToPrettyString(body)} using {ToPrettyString(args.Used)}.");
        _popup.PopupEntity(Loc.GetString("protean-memory-reset"), ent.Owner, args.User);
    }

    private void OnSuitTerminating(Entity<ProteanSuitComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Controller is { } controller && !Deleted(controller))
        {
            _transform.AttachToGridOrMap(controller);
            var restored = _polymorph.Revert(controller);
            if (restored is { } body && TryComp<ProteanComponent>(body, out var protean))
            {
                protean.Folded = false;
                protean.Controller = null;
                protean.Suit = null;
            }
        }

        if (ent.Comp.Owner is { } owner
            && TryComp<ProteanComponent>(owner, out var ownerProtean)
            && ownerProtean.Suit == ent.Owner)
        {
            ownerProtean.Suit = null;
            ownerProtean.Folded = false;
            ownerProtean.Controller = null;
        }
    }

    public bool TryRepair(Entity<ProteanSuitComponent> ent, EntityUid refactory, EntityUid user)
    {
        if (ent.Comp.Owner is not { } body
            || !TryComp<ProteanRefactoryComponent>(refactory, out _)
            || !TryGetOrgan<ProteanBrainComponent>(body, out var brain)
            || !brain.Comp1.Dead
            || !TryComp<ModsuitControlComponent>(ent, out var control)
            || !control.Open
            || !TryComp<BodyComponent>(body, out var bodyComp))
            return false;

        var torso = _body.GetBodyChildrenOfType(body, BodyPartType.Torso, bodyComp).FirstOrDefault();
        if (torso.Id == EntityUid.Invalid
            || !_body.InsertOrgan(torso.Id, refactory, "stomach", torso.Component))
            return false;

        Timer.Spawn(brain.Comp1.RepairDuration, () => FinishRepair(body));
        RaiseSuitChanged(ent);
        _popup.PopupEntity(Loc.GetString("protean-repair-started"), ent.Owner, user);
        return true;
    }

    private void FinishRepair(EntityUid body)
    {
        if (Deleted(body)
            || !TryGetOrgan<ProteanBrainComponent>(body, out var brain)
            || !brain.Comp1.Dead)
            return;

        brain.Comp1.Dead = false;
        Dirty(brain.Owner, brain.Comp1);

        if (TryComp<DamageableComponent>(body, out var damageable))
            _damageable.SetDamage(body, damageable, new DamageSpecifier());

        if (TryComp<MobStateComponent>(body, out var mobState))
            _mobState.ChangeMobState(body, MobState.Alive, mobState);

        if (TryComp<ProteanComponent>(body, out var protean)
            && protean.Suit is { } suit
            && TryComp<ModsuitControlComponent>(suit, out var control))
        {
            control.Open = false;
            Dirty(suit, control);
            RaiseSuitChanged(suit);
        }

        _popup.PopupEntity(Loc.GetString("protean-repaired"), body, body);
    }

    public bool TryEjectSuit(EntityUid user)
    {
        var owner = user;
        if (TryComp<ProteanControllerComponent>(user, out var controller)
            && controller.OriginalBody is { } original)
            owner = original;

        if (!TryComp<ProteanComponent>(owner, out var protean)
            || protean.Suit is not { } suit
            || !TryComp<ProteanSuitComponent>(suit, out var suitComp)
            || suitComp.StoredSuit is not { } stored
            || !TryComp<ModsuitControlComponent>(suit, out var control)
            || control.Active)
            return false;

        var container = _container.EnsureContainer<ContainerSlot>(suit, ProteanSuitContainer.StoredSuit);
        if (!_container.Remove(stored, container, force: true))
            return false;

        foreach (var module in suitComp.AssimilatedModules.ToArray())
        {
            if (!_modules.TryUninstallModule((suit, control), module, silent: true))
                continue;

            if (TryComp<ModsuitControlComponent>(stored, out var storedControl))
            {
                storedControl.Modules.Add(module);
                _container.Insert(module, storedControl.ModuleContainer, force: true);
            }
            else
            {
                _transform.DropNextTo(module, suit);
            }
        }

        suitComp.AssimilatedModules.Clear();

        if (suitComp.StoredTheme is { } theme)
            _modsuits.SetTheme((suit, control), theme, suitComp.StoredSkin);

        suitComp.StoredSuit = null;
        suitComp.StoredTheme = null;
        suitComp.StoredSkin = null;

        if (!protean.Folded && _hands.TryPickupAnyHand(owner, stored))
        {
            // Restored to the owner's hand.
        }
        else
        {
            _transform.DropNextTo(stored, protean.Folded ? suit : owner);
        }

        RaiseSuitChanged(suit);
        _popup.PopupEntity(Loc.GetString("protean-ejected", ("suit", Name(stored))), suit, owner);
        return true;
    }

    private bool TryGetOrgan<T>(EntityUid body, out Entity<T, OrganComponent> organ) where T : IComponent
    {
        if (TryComp<BodyComponent>(body, out var bodyComp)
            && _body.TryGetBodyOrganEntityComps<T>((body, bodyComp), out var organs)
            && organs.Count > 0)
        {
            organ = organs[0];
            return true;
        }

        organ = default;
        return false;
    }

    private void RaiseSuitChanged(EntityUid suit)
    {
        var ev = new ProteanSuitChangedEvent();
        RaiseLocalEvent(suit, ref ev);
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
            _actions.AddAction(body, ref protean.InterfaceActionEntity, "ActionProteanInterface", body);
            _actions.AddAction(body, ref protean.LowPowerActionEntity, "ActionProteanLowPower", body);
            _actions.AddAction(body, ref protean.HealActionEntity, "ActionProteanHeal", body);
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
        _actions.AddAction(body, ref protean.InterfaceActionEntity, "ActionProteanInterface", body);
        _actions.AddAction(body, ref protean.LowPowerActionEntity, "ActionProteanLowPower", body);
        _actions.AddAction(body, ref protean.HealActionEntity, "ActionProteanHeal", body);
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

        _statusEffects.TryRemoveStatusEffect(body, "ProteanLowPowerStatusEffect");
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

        if (TryGetOrgan<ProteanBrainComponent>(body, out var brain) && brain.Comp1.Dead)
        {
            _popup.PopupEntity(Loc.GetString("protean-core-dead"), suit, controller);
            return false;
        }

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
        _actions.AddAction(controller, ref controllerComp.InterfaceActionEntity, "ActionProteanInterface", controller);
        _actions.AddAction(controller, ref controllerComp.HealActionEntity, "ActionProteanHeal", controller);
        _popup.PopupEntity(Loc.GetString("protean-folded"), suit, ent.Owner);
    }
}
