using Content.Shared._PS.Modsuits;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Modsuits;

/// <summary>
/// Implements the TG-style shell assembly: core, screws, four parts, wrench, screws, plating.
/// </summary>
public sealed class ModsuitConstructionSystem : SharedModsuitSystem
{
    private static readonly ProtoId<TagPrototype> ScrewdriverTag = "Screwdriver";
    private static readonly ProtoId<TagPrototype> WrenchTag = "Wrench";

    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ModsuitSystem _modsuits = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModsuitConstructionShellComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<ModsuitConstructionShellComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var used = args.Used;
        var shell = ent.Comp;

        if (!shell.HasCore && HasComp<ModsuitCoreComponent>(used))
        {
            shell.HasCore = true;
            QueueDel(used);
            Popup(ent, args.User, "modsuit-construction-core");
            args.Handled = true;
            return;
        }

        if (shell.HasCore && !shell.CoreSecured && _tags.HasTag(used, ScrewdriverTag))
        {
            shell.CoreSecured = true;
            Popup(ent, args.User, "modsuit-construction-secured-core");
            args.Handled = true;
            return;
        }

        if (shell.CoreSecured && TryComp<ModsuitPartComponent>(used, out var part))
        {
            if (shell.Parts.Add(part.Slot))
            {
                QueueDel(used);
                Popup(ent, args.User, "modsuit-construction-part", ("part", part.Slot.ToString()));
                args.Handled = true;
            }

            return;
        }

        if (shell.Parts.Count == 4 && !shell.Wrenched && _tags.HasTag(used, WrenchTag))
        {
            shell.Wrenched = true;
            Popup(ent, args.User, "modsuit-construction-wrenched");
            args.Handled = true;
            return;
        }

        if (shell.Wrenched && !shell.Secured && _tags.HasTag(used, ScrewdriverTag))
        {
            shell.Secured = true;
            Popup(ent, args.User, "modsuit-construction-secured");
            args.Handled = true;
            return;
        }

        if (shell.Secured && TryComp<ModsuitPlatingComponent>(used, out var plating))
        {
            if (!Prototypes.TryIndex(plating.Theme, out var theme))
                return;

            var control = Spawn("ModsuitControlStandard", Transform(ent.Owner).Coordinates);
            if (TryComp<ModsuitControlComponent>(control, out var controlComp))
                _modsuits.SetTheme((control, controlComp), plating.Theme, theme.DefaultSkin);

            QueueDel(used);
            QueueDel(ent.Owner);
            Popup(ent, args.User, "modsuit-construction-finished");
            args.Handled = true;
            return;
        }

        Popup(ent, args.User, "modsuit-construction-incomplete");
        args.Handled = true;
    }

    private void Popup(EntityUid shell, EntityUid user, string key, params (string, object)[] args)
    {
        var message = args.Length == 0
            ? Loc.GetString(key)
            : Loc.GetString(key, args);

        _popup.PopupEntity(message, shell, user);
    }
}
