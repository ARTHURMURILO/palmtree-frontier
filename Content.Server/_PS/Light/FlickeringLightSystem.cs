using Content.Server.Light.EntitySystems;
using Content.Shared._PS.Light.Components;
using Content.Shared.Light;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;

namespace Content.Server._PS.Light;

/// <summary>
/// Palmtree: keeps a powered light's blinking visuals in sync with the bulb inside it.
/// Flickering bulbs make the fixture blink; rave bulbs take priority and stop the flicker so
/// <see cref="Content.Client._PS.Light.RaveLightSystem"/> can pulse the light instead.
/// Polling is deliberate: the engine event bus only permits one subscription per
/// component/event pair, and the base powered-light system already owns the container events.
/// </summary>
public sealed class FlickeringLightSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly PoweredLightSystem _poweredLight = default!;

    /// <summary>
    /// Powered lights whose blinking was enabled by this system. Lights blinking for other
    /// reasons (such as the station variation pass aging a tube) are left alone.
    /// </summary>
    private readonly HashSet<EntityUid> _managed = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlickeringLightComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _managed.RemoveWhere(uid => !Exists(uid));

        var query = AllEntityQuery<PoweredLightComponent>();
        while (query.MoveNext(out var uid, out var light))
        {
            Refresh((uid, light));
        }
    }

    private void OnMapInit(Entity<FlickeringLightComponent> ent, ref MapInitEvent args)
    {
        // Powered fixtures are handled by the polling in Update once their bulb exists.
        if (HasComp<PoweredLightComponent>(ent))
            return;

        ApplyAlwaysPowered(ent);
    }

    /// <summary>
    /// Always-powered fixtures have no bulb, so there is nothing to keep in sync: turn the
    /// blinking appearance on once and let the client visualizer loop it.
    /// </summary>
    private void ApplyAlwaysPowered(EntityUid uid)
    {
        if (HasComp<RaveLightComponent>(uid))
            return;

        if (!_lights.TryGetLight(uid, out _))
            return;

        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, PoweredLightVisuals.BulbState, PoweredLightState.On, appearance);
        _appearance.SetData(uid, PoweredLightVisuals.Blinking, true, appearance);
    }

    private void Refresh(Entity<PoweredLightComponent> ent)
    {
        var shouldBlink = ShouldBlink(ent);

        if (shouldBlink)
        {
            if (!ent.Comp.IsBlinking)
                _poweredLight.ToggleBlinkingLight(ent.Owner, ent.Comp, true);

            _managed.Add(ent.Owner);
            return;
        }

        if (_managed.Remove(ent.Owner) && ent.Comp.IsBlinking)
            _poweredLight.ToggleBlinkingLight(ent.Owner, ent.Comp, false);
    }

    private bool ShouldBlink(Entity<PoweredLightComponent> ent)
    {
        var bulb = _poweredLight.GetBulb(ent.Owner, ent.Comp);

        var flickering = HasComp<FlickeringLightComponent>(ent)
                         || bulb != null && HasComp<FlickeringLightComponent>(bulb.Value);

        if (!flickering)
            return false;

        var rave = HasComp<RaveLightComponent>(ent)
                   || bulb != null && HasComp<RaveLightComponent>(bulb.Value);

        return !rave;
    }
}
