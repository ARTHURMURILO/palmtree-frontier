using System.Linq;
using Content.Shared._PS.Modsuits;
using Content.Shared._PS.Protean;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffectNew;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._PS.Protean;

/// <summary>
/// Protean interface: metal, fold/unfold and suit lock.
/// </summary>
public sealed class ProteanUiSystem : EntitySystem
{
    [Dependency] private readonly ProteanSystem _protean = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly MovementModStatusSystem _movementStatus = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProteanComponent, ProteanInterfaceEvent>(OnOpenBody);
        SubscribeLocalEvent<ProteanControllerComponent, ProteanInterfaceEvent>(OnOpenController);
        SubscribeLocalEvent<ProteanSuitComponent, ProteanSuitChangedEvent>(OnSuitChanged);
        Subs.BuiEvents<ProteanComponent>(ProteanUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBodyOpened);
            subs.Event<ProteanActionMessage>(OnBodyAction);
        });
        Subs.BuiEvents<ProteanControllerComponent>(ProteanUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnControllerOpened);
            subs.Event<ProteanActionMessage>(OnControllerAction);
        });
    }

    private void OnOpenBody(Entity<ProteanComponent> ent, ref ProteanInterfaceEvent args)
    {
        if (args.Handled)
            return;

        _ui.TryOpenUi(ent.Owner, ProteanUiKey.Key, args.Performer);
        Update(ent.Owner);
        args.Handled = true;
    }

    private void OnOpenController(Entity<ProteanControllerComponent> ent, ref ProteanInterfaceEvent args)
    {
        if (args.Handled)
            return;

        _ui.TryOpenUi(ent.Owner, ProteanUiKey.Key, args.Performer);
        Update(ent.Owner);
        args.Handled = true;
    }

    private void OnBodyOpened(EntityUid uid, ProteanComponent comp, BoundUIOpenedEvent args) => Update(uid);
    private void OnControllerOpened(EntityUid uid, ProteanControllerComponent comp, BoundUIOpenedEvent args) => Update(uid);

    private void OnBodyAction(EntityUid uid, ProteanComponent comp, ProteanActionMessage args)
    {
        switch (args.Action)
        {
            case ProteanUiAction.ToggleFold:
                if (comp.Suit is { } suit)
                    _protean.TryFold(uid, suit);
                break;
            case ProteanUiAction.ToggleLock:
                if (comp.Suit is { } lockSuit)
                    _protean.ToggleLock(uid, lockSuit);
                break;
            case ProteanUiAction.EjectSuit:
                if (!_protean.TryEjectSuit(uid))
                    Update(uid);
                return;
            case ProteanUiAction.ToggleLowPower:
                _protean.TryToggleLowPower(uid, uid);
                break;
            case ProteanUiAction.Heal:
                _protean.TryHeal(uid, uid);
                break;
            case ProteanUiAction.ServoSpeed:
                if (comp.Suit is { } servoBodySuit
                    && TryComp<ModsuitControlComponent>(servoBodySuit, out var servoBodyControl)
                    && servoBodyControl.Wearer is { } servoTarget
                    && servoTarget != uid)
                {
                    _movementStatus.TryAddMovementSpeedModDuration(
                        servoTarget,
                        "ProteanServoMovementStatusEffect",
                        TimeSpan.FromSeconds(7),
                        1.4f);
                }
                break;
        }

        Update(uid);
    }

    private void OnControllerAction(EntityUid uid, ProteanControllerComponent comp, ProteanActionMessage args)
    {
        switch (args.Action)
        {
            case ProteanUiAction.ToggleFold:
                if (!_protean.TryUnfold(uid))
                    Update(uid);
                return;
            case ProteanUiAction.ToggleLock:
                if (comp.Suit is { } suit)
                    _protean.ToggleLock(uid, suit);
                break;
            case ProteanUiAction.EjectSuit:
                if (!_protean.TryEjectSuit(uid))
                    Update(uid);
                return;
            case ProteanUiAction.ToggleLowPower:
                // Low power mode is a body-only toggle; do nothing while folded.
                break;
            case ProteanUiAction.Heal:
                if (comp.OriginalBody is { } healBody)
                    _protean.TryHeal(healBody, uid);
                break;
            case ProteanUiAction.ServoSpeed:
                if (comp.Suit is { } servoControllerSuit
                    && TryComp<ModsuitControlComponent>(servoControllerSuit, out var servoControllerControl)
                    && servoControllerControl.Wearer is { } servoWearer
                    && servoWearer != uid)
                {
                    _movementStatus.TryAddMovementSpeedModDuration(
                        servoWearer,
                        "ProteanServoMovementStatusEffect",
                        TimeSpan.FromSeconds(7),
                        1.4f);
                }
                break;
        }

        if (!Deleted(uid))
            Update(uid);
    }

    private void OnSuitChanged(Entity<ProteanSuitComponent> ent, ref ProteanSuitChangedEvent args)
    {
        if (ent.Comp.Controller is { } controller)
            Update(controller);
        else if (ent.Comp.Owner is { } owner)
            Update(owner);
    }

    private void Update(EntityUid uid)
    {
        if (Deleted(uid))
            return;

        EntityUid? body = null;
        EntityUid? suit = null;
        var folded = false;

        if (TryComp<ProteanComponent>(uid, out var protean))
        {
            body = uid;
            suit = protean.Suit;
            folded = protean.Folded;
        }
        else if (TryComp<ProteanControllerComponent>(uid, out var controller))
        {
            body = controller.OriginalBody;
            suit = controller.Suit;
            folded = true;
        }

        var state = new ProteanUiState
        {
            Folded = folded,
            HasSuit = suit != null,
            CanFold = !folded && suit != null,
            CanUnfold = folded,
        };

        if (TryComp<ProteanSuitComponent>(suit, out var suitComp))
        {
            state.Locked = suitComp.Locked;
            state.HasStoredSuit = suitComp.StoredSuit != null;
        }

        if (TryComp<ModsuitControlComponent>(suit, out var control))
        {
            state.HasServo = control.Modules.Any(module => HasComp<ProteanServoModuleComponent>(module));
            state.ServoReady = state.HasServo && control.Wearer != null && control.Wearer != body;
        }

        if (body is { } statusBody)
            state.LowPower = _statusEffects.HasStatusEffect(statusBody, "ProteanLowPowerStatusEffect");

        if (state.Folded && body is { } healBody)
        {
            if (TryComp<BodyComponent>(healBody, out var healBodyComp)
                && _body.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((healBody, healBodyComp), out var healRefactories)
                && healRefactories.Count > 0)
                state.CanHeal = healRefactories[0].Comp1.Metal >= 6f;
        }

        if (body is { } bodyUid
            && TryComp<BodyComponent>(bodyUid, out var bodyComp)
            && _body.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((bodyUid, bodyComp), out var refactories)
            && refactories.Count > 0)
        {
            state.Metal = refactories[0].Comp1.Metal;
            state.MaxMetal = refactories[0].Comp1.MaxMetal;
        }

        _ui.SetUiState(uid, ProteanUiKey.Key, state);
    }
}
