using Content.Shared._PS.Light.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Light.EntitySystems;

/// <summary>
/// Palmtree: handles screwdriver interaction with rave lights.
/// Using a screwdriver cycles the pulse period through <see cref="RaveLightComponent.Periods"/>.
/// Rave fixtures carry the component directly so the period can be changed while mapped.
/// </summary>
public sealed class SharedRaveLightSystem : EntitySystem
{
    private static readonly ProtoId<ToolQualityPrototype> ScrewingQuality = "Screwing";

    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RaveLightComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<RaveLightComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, ScrewingQuality))
            return;

        ent.Comp.Period = GetNextPeriod(ent.Comp.Period);
        Dirty(ent);

        _popup.PopupPredicted(
            Loc.GetString("rave-light-period-changed", ("period", ent.Comp.Period)),
            ent,
            args.User);

        args.Handled = true;
    }

    private static float GetNextPeriod(float current)
    {
        foreach (var period in RaveLightComponent.Periods)
        {
            if (period > current + 0.001f)
                return period;
        }

        return RaveLightComponent.Periods[0];
    }
}
