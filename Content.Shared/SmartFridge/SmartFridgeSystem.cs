using Content.Shared.Access.Systems;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.SmartFridge;

public sealed class SmartFridgeSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmartFridgeComponent, InteractUsingEvent>(OnInteractUsing, after: [typeof(AnchorableSystem)]);
        SubscribeLocalEvent<SmartFridgeComponent, EntRemovedFromContainerMessage>(OnItemRemoved);

        // Palmtree: the entry list is rebuilt from the container contents instead of being serialized;
        // live saves of a used fridge cannot carry ContainedEntries (see the component's field).
        // ComponentStartup also runs for entities loaded from a ship file (MapInit is skipped for them).
        SubscribeLocalEvent<SmartFridgeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SmartFridgeComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<SmartFridgeComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerb);
        SubscribeLocalEvent<SmartFridgeComponent, GetDumpableVerbEvent>(OnGetDumpableVerb);
        SubscribeLocalEvent<SmartFridgeComponent, DumpEvent>(OnDump);

        Subs.BuiEvents<SmartFridgeComponent>(SmartFridgeUiKey.Key,
            sub =>
            {
                sub.Event<SmartFridgeDispenseItemMessage>(OnDispenseItem);
            });
    }

    /// <summary>
    /// Palmtree: rebuilds the UI entry list from the fridge's container contents. Called on startup
    /// (including ship files loaded without MapInit) and on MapInit (covers spawn-time fills).
    /// </summary>
    private void OnStartup(Entity<SmartFridgeComponent> ent, ref ComponentStartup args)
    {
        RebuildEntries(ent);
    }

    private void OnMapInit(Entity<SmartFridgeComponent> ent, ref MapInitEvent args)
    {
        RebuildEntries(ent);
    }

    /// <summary>
    /// Recreates <see cref="SmartFridgeComponent.ContainedEntries"/> from whatever is inside the
    /// fridge's container, so a fridge that was saved and loaded (or one whose entries were lost)
    /// still lists and dispenses its contents.
    /// </summary>
    public void RebuildEntries(Entity<SmartFridgeComponent> ent)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.Container, out var container))
            return;

        ent.Comp.ContainedEntries.Clear();
        foreach (var contained in container.ContainedEntities)
        {
            var key = new SmartFridgeEntry(Identity.Name(contained, EntityManager));
            if (!ent.Comp.Entries.Contains(key))
                ent.Comp.Entries.Add(key);

            ent.Comp.ContainedEntries.TryAdd(key, new());
            ent.Comp.ContainedEntries[key].Add(GetNetEntity(contained));
        }

        Dirty(ent);
    }

    private bool DoInsert(Entity<SmartFridgeComponent> ent, EntityUid user, IEnumerable<EntityUid> usedItems, bool playSound)
    {        if (!_container.TryGetContainer(ent, ent.Comp.Container, out var container))
            return false;

        if (ent.Comp.CheckAccessOnInsert && !Allowed(ent, user)) // Frontier: add CheckAccessOnInsert
            return true;

        if (ent.Comp.ContainedEntries.Count >= ent.Comp.MaxContainedCount) // Frontier
            return true; // Frontier

        bool anyInserted = false;
        foreach (var used in usedItems)
        {
            if (!_whitelist.CheckBoth(used, ent.Comp.Blacklist, ent.Comp.Whitelist))
                continue;
            anyInserted = true;

            _container.Insert(used, container);
            var key = new SmartFridgeEntry(Identity.Name(used, EntityManager));
            if (!ent.Comp.Entries.Contains(key))
                ent.Comp.Entries.Add(key);

            ent.Comp.ContainedEntries.TryAdd(key, new());
            var entries = ent.Comp.ContainedEntries[key];
            if (!entries.Contains(GetNetEntity(used)))
                entries.Add(GetNetEntity(used));

            Dirty(ent);
        }

        if (anyInserted && playSound)
        {
            _audio.PlayPredicted(ent.Comp.InsertSound, ent, user);
        }

        return anyInserted;
    }

    private void OnInteractUsing(Entity<SmartFridgeComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_hands.CanDrop(args.User, args.Used))
            return;

        args.Handled = DoInsert(ent, args.User, [args.Used], true);
    }

    private void OnItemRemoved(Entity<SmartFridgeComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        var key = new SmartFridgeEntry(Identity.Name(args.Entity, EntityManager));

        if (ent.Comp.ContainedEntries.TryGetValue(key, out var contained))
        {
            contained.Remove(GetNetEntity(args.Entity));
            // Frontier: remove listing when empty
            if (contained.Count <= 0)
            {
                ent.Comp.ContainedEntries.Remove(key);
                ent.Comp.Entries.Remove(key);
            }
            // End Frontier: remove listing when empty
        }

        Dirty(ent);
    }

    private bool Allowed(Entity<SmartFridgeComponent> machine, EntityUid user)
    {
        if (_accessReader.IsAllowed(user, machine))
            return true;

        _popup.PopupPredicted(Loc.GetString("smart-fridge-component-try-eject-access-denied"), machine, user);
        _audio.PlayPredicted(machine.Comp.SoundDeny, machine, user);
        return false;
    }

    private void OnDispenseItem(Entity<SmartFridgeComponent> ent, ref SmartFridgeDispenseItemMessage args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!Allowed(ent, args.Actor))
            return;

        if (!ent.Comp.ContainedEntries.TryGetValue(args.Entry, out var contained))
        {
            _audio.PlayPredicted(ent.Comp.SoundDeny, ent, args.Actor);
            _popup.PopupPredicted(Loc.GetString("smart-fridge-component-try-eject-unknown-entry"), ent, args.Actor);
            return;
        }

        foreach (var item in contained)
        {
            if (!_container.TryRemoveFromContainer(GetEntity(item)))
                continue;

            _audio.PlayPredicted(ent.Comp.SoundVend, ent, args.Actor);
            contained.Remove(item);
            // Frontier: remove listing when empty
            if (contained.Count <= 0)
            {
                ent.Comp.ContainedEntries.Remove(args.Entry);
                ent.Comp.Entries.Remove(args.Entry);
            }
            // End Frontier: remove listing when empty
            Dirty(ent);
            return;
        }

        _audio.PlayPredicted(ent.Comp.SoundDeny, ent, args.Actor);
        _popup.PopupPredicted(Loc.GetString("smart-fridge-component-try-eject-out-of-stock"), ent, args.Actor);
    }

    private void OnGetAltVerb(Entity<SmartFridgeComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        var user = args.User;

        if (!args.CanInteract
            || args.Using is not { } item
            || !_hands.CanDrop(user, item)
            || !_whitelist.CheckBoth(item, ent.Comp.Blacklist, ent.Comp.Whitelist))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => DoInsert(ent, user, [item], true),
            Text = Loc.GetString("verb-categories-insert"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/insert.svg.192dpi.png")),
        });
    }

    private void OnGetDumpableVerb(Entity<SmartFridgeComponent> ent, ref GetDumpableVerbEvent args)
    {
        if (!ent.Comp.CheckAccessOnInsert || _accessReader.IsAllowed(args.User, ent)) // Frontier: add CheckAccessOnInsert
        {
            args.Verb = Loc.GetString("dump-smartfridge-verb-name", ("unit", ent));
        }
    }

    private void OnDump(Entity<SmartFridgeComponent> ent, ref DumpEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        args.PlaySound = true;

        DoInsert(ent, args.User, args.DumpQueue, false);
    }
}
