using Content.Shared._PS.Atmos.Components;
using Content.Shared.Examine;

namespace Content.Shared._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: shared logic for the atmos meter. The readings are refreshed by the server system; this
/// only turns them into examine text.
/// </summary>
public abstract class SharedGasMeterSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasMeterComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<GasMeterComponent> ent, ref ExaminedEvent args)
    {
        if (!Transform(ent).Anchored || !args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(GasMeterComponent)))
        {
            args.PushMarkup(Loc.GetString("gas-meter-examined-pressure",
                ("pressure", $"{ent.Comp.Pressure:0.#}")));

            args.PushMarkup(Loc.GetString("gas-meter-examined-temperature",
                ("temperature", $"{ent.Comp.Temperature:0.#}"),
                ("celsius", $"{ent.Comp.Temperature - 273.15f:0.#}")));
        }
    }
}
