// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server._NSV13.Reactor.Components;
using Content.Shared.Burial.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     The stormdrive's meltdown recovery chain.
/// </summary>
/// <remarks>
///     <para>
///     Ported from the <c>attackby</c>/<c>welder_act</c> half of <c>stormdrive.dm</c>. A wrecked core
///     is not scrap: it is a four-step repair job.
///     </para>
///     <list type="number">
///     <item>Clear the plutonium sludge out of the pit with a shovel.</item>
///     <item>Reline the pit with twenty-five sheets of liner material.</item>
///     <item>Weld the new liner down.</item>
///     <item>Drop a fresh reactor core in, which factory-resets the machine.</item>
///     </list>
///     <para>
///     PORT NOTE: the original relines with duranium, which this fork has no prototype for. Uranium
///     sheet is the closest existing material and keeps the "expensive metal liner" meaning.
///     </para>
/// </remarks>
public sealed class StormdriveRepairSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly StormdriveSystem _stormdrive = default!;

    /// <summary>Sheets of liner material a reline costs.</summary>
    public const int RelineCost = 25;

    /// <summary>
    ///     Stack type the pit is relined with. <c>PORT NOTE</c>: the DM original required duranium,
    ///     which this fork has no prototype for; uranium sheet is the documented substitute. Keep
    ///     this a gate - accepting any stack let a plank of wood reline a nuclear reactor pit.
    /// </summary>
    private const string LinerStackType = "Uranium";

    private static readonly TimeSpan StepTime = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();

        // NOTE: the InteractUsingEvent subscription lives in StormdriveRodSystem: the rod bay and the
        // repair chain share the same verb, so one handler decides which of the two owns the item.
        SubscribeLocalEvent<StormdriveComponent, StormdriveRepairDoAfterEvent>(OnRepairStepFinished);
    }

    /// <summary>
    ///     Called from <see cref="StormdriveRodSystem"/> for items that are not control rods.
    /// </summary>
    /// <returns>True when the interaction was consumed.</returns>
    public bool TryHandleInteract(EntityUid uid, StormdriveComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return false;

        var item = args.Used;

        switch (comp.State)
        {
            case StormdriveState.Meltdown:
                if (!HasComp<ShovelComponent>(item))
                    return false;

                args.Handled = true;
                StartStep(uid, args.User, item, StormdriveRepairStep.ClearSludge);
                return true;

            case StormdriveState.Repair:
                if (!TryComp<StackComponent>(item, out var stack) || stack.StackTypeId != LinerStackType)
                    return false;

                if (_stack.GetCount(item, stack) < RelineCost)
                {
                    args.Handled = true;
                    _popup.PopupEntity(
                        Loc.GetString("stormdrive-repair-need-sheets", ("count", RelineCost)), uid, args.User);
                    return false;
                }

                args.Handled = true;
                StartStep(uid, args.User, item, StormdriveRepairStep.Reline);
                return true;

            case StormdriveState.Reinforce:
                if (!HasComp<WelderComponent>(item))
                    return false;

                args.Handled = true;
                StartStep(uid, args.User, item, StormdriveRepairStep.Weld);
                return true;

            case StormdriveState.Refit:
                if (!HasComp<StormdriveCoreComponent>(item))
                    return false;

                args.Handled = true;
                StartStep(uid, args.User, item, StormdriveRepairStep.InstallCore);
                return true;
        }

        return false;
    }

    private void StartStep(EntityUid uid, EntityUid user, EntityUid item, StormdriveRepairStep step)
    {
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, StepTime,
            new StormdriveRepairDoAfterEvent { Step = step }, uid, target: uid, used: item)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnRepairStepFinished(EntityUid uid, StormdriveComponent comp, StormdriveRepairDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        switch (args.Step)
        {
            case StormdriveRepairStep.ClearSludge:
                if (comp.State != StormdriveState.Meltdown)
                    return;

                _popup.PopupEntity(Loc.GetString("stormdrive-repair-sludge-spill"), uid, args.User);
                Spawn(comp.SlagPrototype, Transform(uid).Coordinates);
                _audio.PlayPvs("/Audio/Effects/Fluids/glug.ogg", uid);
                comp.State = StormdriveState.Repair;
                break;

            case StormdriveRepairStep.Reline:
                if (comp.State != StormdriveState.Repair || args.Used is not { } sheets)
                    return;

                if (!TryComp<StackComponent>(sheets, out var stack) || !_stack.Use(sheets, RelineCost, stack))
                    return;

                _popup.PopupEntity(Loc.GetString("stormdrive-repair-relined"), uid, args.User);
                _audio.PlayPvs("/Audio/Items/welder.ogg", uid);
                comp.State = StormdriveState.Reinforce;
                break;

            case StormdriveRepairStep.Weld:
                if (comp.State != StormdriveState.Reinforce)
                    return;

                _popup.PopupEntity(Loc.GetString("stormdrive-repair-welded"), uid, args.User);
                _audio.PlayPvs("/Audio/Items/welder2.ogg", uid);
                comp.State = StormdriveState.Refit;
                break;

            case StormdriveRepairStep.InstallCore:
                if (comp.State != StormdriveState.Refit || args.Used is not { } core)
                    return;

                _popup.PopupEntity(Loc.GetString("stormdrive-repair-core-installed"), uid, args.User);
                _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);

                // DM: qdel(I); control_rods = list(); for everything in contents: qdel; deactivate().
                var container = _container.EnsureContainer<Container>(uid, StormdriveComponent.RodContainer);
                foreach (var rod in comp.ControlRods)
                {
                    if (Exists(rod))
                        QueueDel(rod);
                }

                foreach (var entity in container.ContainedEntities.ToArray())
                    QueueDel(entity);

                comp.ControlRods.Clear();
                _stormdrive.RecalculateRods(comp);

                // The DM original simply calls deactivate(): that resets heat, reaction rate,
                // stability, last power and the grid supply, and drops the state to Idle. Calling the
                // shared helper keeps the factory reset in one place; a hand-rolled partial reset
                // previously left stability and the power supplier at their pre-meltdown values.
                _stormdrive.Deactivate(uid, comp);
                comp.WarningState = StormdriveWarning.None;
                QueueDel(core);
                break;
        }

        _stormdrive.UpdateVisuals(uid, comp);
    }
}
