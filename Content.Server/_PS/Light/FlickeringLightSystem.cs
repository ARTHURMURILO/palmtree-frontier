using Content.Server.Light.EntitySystems;
using Content.Shared._PS.Light.Components;
using Content.Shared.Light.Components;

namespace Content.Server._PS.Light;

/// <summary>
/// Palmtree: enables the blinking visuals for flickering light fixtures.
/// The fixture owns the effect; its bulb only supplies the light's color and point-light values.
/// </summary>
public sealed class FlickeringLightSystem : EntitySystem
{
    [Dependency] private readonly PoweredLightSystem _poweredLight = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlickeringLightComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<FlickeringLightComponent, MapInitEvent>(OnMapInit);
    }

    private void OnComponentStartup(Entity<FlickeringLightComponent> ent, ref ComponentStartup args)
    {
        // MapInit handles fixtures loaded from prototypes; this only matters when the component
        // is added at runtime after all of the fixture's components exist.
        if (LifeStage(ent) < EntityLifeStage.MapInitialized)
            return;

        TryEnable(ent);
    }

    private void OnMapInit(Entity<FlickeringLightComponent> ent, ref MapInitEvent args)
    {
        TryEnable(ent);
    }

    private void TryEnable(Entity<FlickeringLightComponent> ent)
    {
        if (HasComp<RaveLightComponent>(ent))
            return;

        if (!TryComp<PoweredLightComponent>(ent, out var light))
            return;

        _poweredLight.ToggleBlinkingLight(ent.Owner, light, true);
    }
}
