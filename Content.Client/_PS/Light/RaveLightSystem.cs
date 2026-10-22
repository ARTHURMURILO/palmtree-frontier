using Content.Shared._PS.Light.Components;
using Content.Shared.Light;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Client._PS.Light;

/// <summary>
/// Palmtree: client-side pulse for <see cref="RaveLightComponent"/>.
/// The point light and its glow layer are animated locally, so the server does not have to dirty
/// and network the light component every tick.
/// </summary>
public sealed class RaveLightSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime.TotalSeconds;

        var directQuery = AllEntityQuery<RaveLightComponent, PointLightComponent>();
        while (directQuery.MoveNext(out var uid, out var rave, out var light))
        {
            Process(uid, rave, light, now);
        }

        // Rave bulbs live inside a fixture, so look them up through the fixture's bulb container.
        var poweredQuery = AllEntityQuery<PoweredLightComponent, PointLightComponent>();
        while (poweredQuery.MoveNext(out var uid, out _, out var light))
        {
            if (HasComp<RaveLightComponent>(uid))
                continue;

            if (!TryGetRaveBulb(uid, out var rave))
                continue;

            Process(uid, rave, light, now);
        }
    }

    private void Process(EntityUid uid, RaveLightComponent rave, PointLightComponent light, double now)
    {
        var baseEnergy = rave.MaxEnergy;
        if (baseEnergy <= 0f)
        {
            // Fallback for lights without an explicit peak energy: remember the energy the
            // server last set before we started changing it locally.
            if (rave.CachedBaseEnergy <= 0f)
                rave.CachedBaseEnergy = light.Energy;

            baseEnergy = rave.CachedBaseEnergy;
        }

        var period = MathF.Max(rave.Period, 0.05f);
        var phase = (float) (now / period) + rave.Phase;
        var cycle = phase - MathF.Floor(phase);
        var brightness = 1f - MathF.Abs(2f * cycle - 1f);

        _lights.SetEnergy(uid, baseEnergy * brightness, light);

        if (!TryComp<SpriteComponent>(uid, out var sprite)
            || !_sprites.LayerExists((uid, sprite), PoweredLightLayers.Glow))
        {
            return;
        }

        var color = light.Color;
        _sprites.LayerSetColor(
            (uid, sprite),
            PoweredLightLayers.Glow,
            new Color(color.R * brightness, color.G * brightness, color.B * brightness, color.A * brightness));
    }

    private bool TryGetRaveBulb(EntityUid uid, out RaveLightComponent rave)
    {
        rave = default!;

        if (!_containers.TryGetContainer(uid, SharedPoweredLightSystem.LightBulbContainer, out var container))
            return false;

        foreach (var contained in container.ContainedEntities)
        {
            if (!TryComp(contained, out RaveLightComponent? comp))
                continue;

            rave = comp;
            return true;
        }

        return false;
    }
}
