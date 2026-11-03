using Content.Shared._PS.Light.Components;
using Content.Shared.Light;
using Content.Shared.Light.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._PS.Light;

/// <summary>
/// Palmtree: client-side pulse for <see cref="RaveLightComponent"/> on light fixtures.
/// The point light and its glow layer are animated locally, so the server does not have to dirty
/// and network the light component every tick. The fixture's bulb only determines the light's color.
/// </summary>
public sealed class RaveLightSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime.TotalSeconds;

        var query = AllEntityQuery<RaveLightComponent, PointLightComponent>();
        while (query.MoveNext(out var uid, out var rave, out var light))
        {
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
}
