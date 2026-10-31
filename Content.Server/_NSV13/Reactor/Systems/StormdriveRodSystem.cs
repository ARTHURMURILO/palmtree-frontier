// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server.Emp;
using Content.Shared.Examine;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Server.Radiation.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Handles the stormdrive's rod bay: installing and pulling control rods by hand, and the window
///     that lists what is mounted.
/// </summary>
/// <remarks>
///     Ported from the <c>attackby</c>/<c>ui_act</c>/<c>ui_data</c> half of <c>stormdrive.dm</c>.
///     The DM reactor let you open the lid in maintenance, or - with heavy personal consequences -
///     reach into a live core. Both paths are here; the only thing dropped is arm dismemberment,
///     because SS14 has no body-part loss.
/// </remarks>
public sealed class StormdriveRodSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly RadiationSystem _radiation = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StormdriveSystem _stormdrive = default!;
    [Dependency] private readonly StormdriveRepairSystem _repair = default!;

    /// <summary>Hands-on installation time, matching the DM <c>do_after(user, 50)</c>.</summary>
    private static readonly TimeSpan InstallTime = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StormdriveComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<StormdriveComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<StormdriveComponent, StormdriveRodInstallDoAfterEvent>(OnInstallFinished);
        SubscribeLocalEvent<StormdriveComponent, EntInsertedIntoContainerMessage>(OnRodInserted);
        SubscribeLocalEvent<StormdriveComponent, EntRemovedFromContainerMessage>(OnRodRemoved);
        SubscribeLocalEvent<StormdriveComponent, StormdriveRodRemoveDoAfterEvent>(OnRemoveFinished);
        SubscribeLocalEvent<StormdriveComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<StormdriveComponent, StormdriveRemoveRodMessage>(OnRemoveMessage);
    }

    private void OnExamined(EntityUid uid, StormdriveComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("stormdrive-examine-ports"));
    }

    /// <summary>The bay's container, created on first use.</summary>
    public Container GetRodContainer(EntityUid uid)
    {
        return _container.EnsureContainer<Container>(uid, StormdriveComponent.RodContainer);
    }

    /// <summary>§<c>attackby</c>: mount a control rod, by hand, into the bay.</summary>
    private void OnInteractUsing(EntityUid uid, StormdriveComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ControlRodComponent>(args.Used, out _))
        {
            // Sludge shovelling, liner sheets, welding and the new core all share this verb.
            _repair.TryHandleInteract(uid, comp, args);
            return;
        }

        args.Handled = true;

        if (comp.State is StormdriveState.Meltdown or >= StormdriveState.Repair)
        {
            _popup.PopupEntity(Loc.GetString("stormdrive-rods-no-bay"), uid, args.User);
            return;
        }

        if (comp.ControlRods.Count >= comp.ControlRodCount)
        {
            _popup.PopupEntity(Loc.GetString("stormdrive-rods-full"), uid, args.User);
            return;
        }

        var live = comp.State == StormdriveState.Running;

        if (!live && comp.State != StormdriveState.Maintenance)
        {
            _popup.PopupEntity(Loc.GetString("stormdrive-rods-not-maintenance"), uid, args.User);
            return;
        }

        if (live)
        {
            // DM: reaching into a running core. You keep the hand, but it costs you.
            _popup.PopupEntity(Loc.GetString("stormdrive-rods-live-blue-glow"), uid, args.User, PopupType.LargeCaution);
            _audio.PlayPvs("/Audio/Items/welder.ogg", uid);

            _emp.EmpPulse(_transform.GetMapCoordinates(uid), 3f, 5f, 5f);

            ZapUser(args.User, 20f);
            _radiation.IrradiateEntity(args.User, 25f, 1f);
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, InstallTime,
            new StormdriveRodInstallDoAfterEvent(), uid, target: uid, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnInstallFinished(EntityUid uid, StormdriveComponent comp, StormdriveRodInstallDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (args.Used is not { } rodUid || !Exists(rodUid) || !TryComp<ControlRodComponent>(rodUid, out _))
            return;

        if (comp.ControlRods.Count >= comp.ControlRodCount)
            return;

        args.Handled = true;

        // Re-check maintenance: somebody may have closed the lid mid-way, which is exactly the
        // condition the DM version re-checked after its do_after.
        if (comp.State != StormdriveState.Maintenance && comp.State != StormdriveState.Running)
            return;

        // Bookkeeping (list, efficiency, sprite) is driven by the container events below, so the
        // same path serves hand-mounted rods, ContainerFill and any future loader.
        if (!_container.Insert(rodUid, GetRodContainer(uid)))
            return;

        _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        _popup.PopupEntity(Loc.GetString("stormdrive-rods-mounted"), uid, args.User);
        UpdateUi(uid, comp);
    }

    /// <summary>§<c>ui_act "remove_rod"</c>: pull a rod back out.</summary>
    private void OnRemoveMessage(EntityUid uid, StormdriveComponent comp, StormdriveRemoveRodMessage args)
    {
        if (args.Index < 0 || args.Index >= comp.ControlRods.Count)
            return;

        if (comp.State is StormdriveState.Meltdown or >= StormdriveState.Repair)
            return;

        if (comp.State != StormdriveState.Maintenance && comp.State != StormdriveState.Running)
        {
            Notice(comp, Loc.GetString("stormdrive-rods-not-maintenance"));
            UpdateUi(uid, comp);
            return;
        }

        if (comp.State == StormdriveState.Running)
        {
            // §ui_act "remove_rod" on a live core: the DM warned you, then either the gloves held
            // (EMP + radiation + a burnt hand) or the rod went through it. There is no dismemberment
            // in this port, so the gloves always hold - it just costs you.
            _popup.PopupEntity(Loc.GetString("stormdrive-rods-live-blue-glow"), args.Actor, args.Actor,
                PopupType.LargeCaution);
            _audio.PlayPvs("/Audio/Items/welder.ogg", uid);
            _emp.EmpPulse(_transform.GetMapCoordinates(uid), 3f, 5f, 5f);
            ZapUser(args.Actor, 20f);
            _radiation.IrradiateEntity(args.Actor, 25f, 1f);
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.Actor, InstallTime,
            new StormdriveRodRemoveDoAfterEvent { Index = args.Index }, uid, target: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnRemoveFinished(EntityUid uid, StormdriveComponent comp, StormdriveRodRemoveDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (args.Index < 0 || args.Index >= comp.ControlRods.Count)
            return;

        var rodUid = comp.ControlRods[args.Index];
        _container.Remove(rodUid, GetRodContainer(uid));

        // The DM version forceMove()d the rod to the operator's turf.
        if (Exists(rodUid))
            _transform.SetCoordinates(rodUid, Transform(uid).Coordinates);

        _stormdrive.UpdateVisuals(uid, comp);

        _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        UpdateUi(uid, comp);
    }

    /// <summary>Damage the operator the way a live core does - burns, no dismemberment.</summary>
    private void ZapUser(EntityUid user, float amount)
    {
        if (!TryComp<DamageableComponent>(user, out var damageable))
            return;

        _damageable.TryChangeDamage(user, new DamageSpecifier { DamageDict = { ["Heat"] = amount } },
            origin: user, damageable: damageable);
    }

    /// <summary>
    ///     Keep <see cref="StormdriveComponent.ControlRods"/> in step with the bay container. This is
    ///     the single place rods join or leave the simulation, so ContainerFill-loaded rods (the
    ///     pre-started prototype, mappers) and hand-mounted rods behave identically.
    /// </summary>
    private void OnRodInserted(EntityUid uid, StormdriveComponent comp, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != StormdriveComponent.RodContainer)
            return;

        if (!HasComp<ControlRodComponent>(args.Entity) || comp.ControlRods.Contains(args.Entity))
            return;

        comp.ControlRods.Add(args.Entity);
        _stormdrive.RecalculateRods(comp);
        _stormdrive.UpdateVisuals(uid, comp);
        UpdateUi(uid, comp);
    }

    private void OnRodRemoved(EntityUid uid, StormdriveComponent comp, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != StormdriveComponent.RodContainer)
            return;

        if (!comp.ControlRods.Remove(args.Entity))
            return;

        _stormdrive.RecalculateRods(comp);
        _stormdrive.UpdateVisuals(uid, comp);
        UpdateUi(uid, comp);
    }

    private void OnUiOpened(EntityUid uid, StormdriveComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp);
    }

    private void Notice(StormdriveComponent comp, string message)
    {
        comp.RodNotice = message;
        comp.RodNoticeUntil = _timing.CurTime + TimeSpan.FromSeconds(4);
    }

    public void UpdateUi(EntityUid uid, StormdriveComponent comp)
    {
        if (!_ui.HasUi(uid, StormdriveRodsUiKey.Key))
            return;

        if (comp.RodNotice.Length > 0 && _timing.CurTime >= comp.RodNoticeUntil)
            comp.RodNotice = string.Empty;

        var state = new StormdriveRodsBoundUserInterfaceState
        {
            Capacity = comp.ControlRodCount,
            Maintenance = comp.State == StormdriveState.Maintenance,
            Broken = comp.State is StormdriveState.Meltdown or >= StormdriveState.Repair,
            Notice = comp.RodNotice,
        };

        foreach (var rodUid in comp.ControlRods)
        {
            if (!TryComp<ControlRodComponent>(rodUid, out var rod))
                continue;

            state.Rods.Add(new StormdriveRodEntry
            {
                Name = MetaData(rodUid).EntityName,
                Percent = rod.MaxIntegrity <= 0f ? 0f : Math.Clamp(rod.Integrity / rod.MaxIntegrity * 100f, 0f, 100f),
                Effectiveness = rod.Effectiveness,
                Spent = rod.Spent,
            });
        }

        _ui.SetUiState(uid, StormdriveRodsUiKey.Key, state);
    }
}
