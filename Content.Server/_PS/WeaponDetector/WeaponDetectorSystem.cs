using Content.Server.Power.EntitySystems;
using Content.Shared._PS.WeaponDetector;
using Content.Shared.Access.Systems;
using Content.Shared.Examine;
using Content.Shared.Mindshield.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._PS.WeaponDetector;

/// <summary>
/// Walk-through weapon detector: beeps when an entity carrying a gun steps onto its tile
/// (SS13 scanner gate, guns mode). Variants differ only by <see cref="WeaponDetectorScanMode"/>.
/// The entity needs a hard:false sensor fixture on the same tile (see the prototype) for
/// <see cref="StepTriggerSystem"/> to see contacts; the attempt handler shuts scanning off
/// while unanchored or unpowered.
/// </summary>
public sealed class WeaponDetectorSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeaponDetectorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WeaponDetectorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WeaponDetectorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<WeaponDetectorComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<WeaponDetectorComponent, StepTriggerAttemptEvent>(OnStepAttempt);
        SubscribeLocalEvent<WeaponDetectorComponent, StepTriggeredOnEvent>(OnStepTriggered);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WeaponDetectorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ResetAt == TimeSpan.Zero || now < comp.ResetAt)
                continue;

            comp.ResetAt = TimeSpan.Zero;
            SetVisual((uid, comp), GetIdleState(uid, comp));
        }
    }

    private void OnMapInit(Entity<WeaponDetectorComponent> ent, ref MapInitEvent args)
    {
        SetVisual(ent, GetIdleState(ent.Owner, ent.Comp));
    }

    private void OnExamined(Entity<WeaponDetectorComponent> ent, ref ExaminedEvent args)
    {
        // Power state is covered by ApcPowerReceiverComponent's own examine text.
        if (ent.Comp.RequiresAnchored && !Transform(ent).Anchored)
            args.PushMarkup(Loc.GetString("weapon-detector-examine-unanchored"));
    }

    private void OnPowerChanged(Entity<WeaponDetectorComponent> ent, ref PowerChangedEvent args)
    {
        // Don't stomp an in-progress scan/alarm flash; the Update loop restores the idle state.
        if (ent.Comp.ResetAt != TimeSpan.Zero)
            return;

        SetVisual(ent, GetIdleState(ent.Owner, ent.Comp));
    }

    private void OnAnchorChanged(Entity<WeaponDetectorComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (ent.Comp.ResetAt != TimeSpan.Zero)
            return;

        SetVisual(ent, GetIdleState(ent.Owner, ent.Comp));
    }

    private void OnStepAttempt(Entity<WeaponDetectorComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue = IsReady(ent.Owner, ent.Comp);
    }

    private void OnStepTriggered(Entity<WeaponDetectorComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (!IsReady(ent.Owner, ent.Comp))
            return;

        var comp = ent.Comp;
        var now = _timing.CurTime;

        if (!HasGun(args.Tripper, comp))
        {
            Flash(ent, WeaponDetectorState.Scanning, now);
            return;
        }

        // SS13: mindshield carriers and IDs with weapons access don't set the gate off.
        if (comp.ScanMode == WeaponDetectorScanMode.SecurityBypass && HasBypass(args.Tripper, comp))
        {
            _popup.PopupEntity(Loc.GetString(comp.BypassPopup), ent.Owner, PopupType.Small);
            Flash(ent, WeaponDetectorState.Scanning, now);
            return;
        }

        Flash(ent, WeaponDetectorState.Alarm, now);

        if (now < comp.NextBeep)
            return;

        comp.NextBeep = now + comp.BeepCooldown;
        _audio.PlayPvs(comp.AlarmSound, ent.Owner);
        _popup.PopupEntity(Loc.GetString(comp.AlarmPopup), ent.Owner, PopupType.LargeCaution);
    }

    private bool IsReady(EntityUid uid, WeaponDetectorComponent comp)
    {
        if (comp.RequiresAnchored && !Transform(uid).Anchored)
            return false;

        if (comp.RequiresPower && !this.IsPowered(uid, EntityManager))
            return false;

        return true;
    }

    private WeaponDetectorState GetIdleState(EntityUid uid, WeaponDetectorComponent comp)
    {
        return IsReady(uid, comp) ? WeaponDetectorState.Passive : WeaponDetectorState.Off;
    }

    private void Flash(Entity<WeaponDetectorComponent> ent, WeaponDetectorState state, TimeSpan now)
    {
        ent.Comp.ResetAt = now + ent.Comp.VisualDuration;
        SetVisual(ent, state);
    }

    private void SetVisual(Entity<WeaponDetectorComponent> ent, WeaponDetectorState state)
    {
        if (ent.Comp.VisualState == state)
            return;

        ent.Comp.VisualState = state;
        Dirty(ent);
        _appearance.SetData(ent, WeaponDetectorVisuals.State, state);
    }

    /// <summary>
    /// True when the tripper itself is a gun or carries one in hands, inventory, or nested storage.
    /// Containers ride on the transform tree, so a recursive child walk covers all of them.
    /// </summary>
    private bool HasGun(EntityUid tripper, WeaponDetectorComponent comp)
    {
        if (IsCountedGun(tripper, comp))
            return true;

        var enumerator = Transform(tripper).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (CheckTree(child, comp))
                return true;
        }

        return false;
    }

    private bool CheckTree(EntityUid uid, WeaponDetectorComponent comp)
    {
        if (IsCountedGun(uid, comp))
            return true;

        var enumerator = Transform(uid).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (CheckTree(child, comp))
                return true;
        }

        return false;
    }

    private bool HasBypass(EntityUid tripper, WeaponDetectorComponent comp)
    {
        if (HasComp<MindShieldComponent>(tripper))
            return true;

        return _access.FindAccessTags(tripper).Contains(comp.BypassAccess);
    }

    private bool IsCountedGun(EntityUid uid, WeaponDetectorComponent comp)
    {
        if (!HasComp<GunComponent>(uid))
            return false;

        // Toy guns only count for non-lethal-only modes.
        return comp.ScanMode != WeaponDetectorScanMode.LethalOnly || !_tag.HasTag(uid, comp.ToyTag);
    }
}
