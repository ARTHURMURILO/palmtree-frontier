using Content.Server.Atmos.Piping.EntitySystems;
using Content.Server._PS.Construction.Components;
using Content.Shared.Administration.Logs;
using Content.Shared._PS.Construction;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;

namespace Content.Server._PS.Construction;

/// <summary>
///     Gives a deployed machine a context-menu action that packs it back into its flatpack after a
///     short do-after.
/// </summary>
public sealed class PackableIntoFlatpackSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly AtmosUnsafeUnanchorSystem _atmosUnsafeUnanchor = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PackableIntoFlatpackComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<PackableIntoFlatpackComponent, PackIntoFlatpackDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<PackableIntoFlatpackComponent, ExaminedEvent>(OnExamined);
    }

    private void OnGetVerbs(Entity<PackableIntoFlatpackComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("pack-into-flatpack-verb"),
            Act = () => StartPacking(ent, user),
            Impact = LogImpact.Medium,
        });
    }

    private void StartPacking(Entity<PackableIntoFlatpackComponent> ent, EntityUid user)
    {
        var args = new DoAfterArgs(EntityManager,
            user,
            ent.Comp.PackDelay,
            new PackIntoFlatpackDoAfterEvent(),
            ent,
            target: ent)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(args);
    }

    private void OnDoAfter(Entity<PackableIntoFlatpackComponent> ent, ref PackIntoFlatpackDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // Two players can finish their do-afters on the same tick. The first already handed the
        // flatpack back and queued the part for deletion, so the second must not refund another.
        if (TerminatingOrDeleted(ent.Owner) || EntityManager.IsQueuedForDeletion(ent.Owner))
            return;

        args.Handled = true;

        // A packed machine is deleted, so empty its pipe nodes into the tile first. This is the
        // same leak the atmos-unsafe unanchor path performs; without it a charged circulator
        // would silently delete its pipe gas with the part.
        _atmosUnsafeUnanchor.LeakGas(ent.Owner);

        var xform = Transform(ent.Owner);
        var flatpack = Spawn(ent.Comp.Flatpack, xform.Coordinates);
        _transform.SetLocalRotation(flatpack, xform.LocalRotation);

        _popup.PopupEntity(Loc.GetString("pack-into-flatpack-done"), ent, args.User);
        _adminLogger.Add(LogType.Construction,
            LogImpact.Medium,
            $"{ToPrettyString(args.User):player} packed {ToPrettyString(ent.Owner):entity} into {ToPrettyString(flatpack):entity}");

        QueueDel(ent.Owner);
    }

    private void OnExamined(Entity<PackableIntoFlatpackComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("pack-into-flatpack-examine"));
    }
}
