using Content.Shared._PS.Interactions;
using Content.Shared.Buckle.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._PS.Interactions;

/// <summary>
/// Palmtree: applies the interaction panel's character layer preference and keeps living mobs'
/// draw depth synchronized. Runs once per second: living, unbuckled mobs are returned to their
/// chosen layer, which clears stale draw-depth values left behind by other visual states (the old
/// workaround was buckling on a chair and unbuckling).
/// </summary>
public sealed class CharacterLayerSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    /// <summary>Baseline draw depth for mobs that do not set their own; humanoids use Mobs.</summary>
    private readonly Dictionary<EntityUid, int> _baseDrawDepths = new();

    private float _syncAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateComponent, ComponentShutdown>(OnMobShutdown);
    }

    private void OnMobShutdown(EntityUid uid, MobStateComponent component, ComponentShutdown args)
    {
        _baseDrawDepths.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _syncAccumulator += frameTime;
        if (_syncAccumulator < 1f)
            return;

        _syncAccumulator %= 1f;

        var query = EntityQueryEnumerator<MobStateComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var mobState, out var sprite))
        {
            // Buckling owns the draw depth while strapped in; the buckle system restores it on
            // unbuckle, and this system repairs any stale value on the next sync.
            if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
                continue;

            // Dead mobs are drawn at the death layer by the damage-state visualizer.
            if (mobState.CurrentState == MobState.Dead)
                continue;

            var baseDepth = GetBaseDrawDepth(uid, sprite);
            var mode = TryComp<InteractionPanelComponent>(uid, out var panel)
                ? panel.LayerMode
                : CharacterLayerMode.Default;

            var expected = GetDrawDepth(baseDepth, mode);
            if (sprite.DrawDepth == expected)
                continue;

            _sprite.SetDrawDepth((uid, sprite), expected);
        }
    }

    /// <summary>
    /// Humanoids are always based on the standard mob layer, which lets the watchdog repair stale
    /// values even if the first frame it saw was already wrong. Other living mobs keep whatever
    /// draw depth they first appeared with (e.g. small mobs below tables).
    /// </summary>
    private int GetBaseDrawDepth(EntityUid uid, SpriteComponent sprite)
    {
        if (HasComp<HumanoidAppearanceComponent>(uid))
            return (int) DrawDepth.Mobs;

        if (!_baseDrawDepths.TryGetValue(uid, out var baseDepth))
        {
            baseDepth = sprite.DrawDepth;
            _baseDrawDepths[uid] = baseDepth;
        }

        return baseDepth;
    }

    private static int GetDrawDepth(int baseDepth, CharacterLayerMode mode)
    {
        return mode switch
        {
            CharacterLayerMode.Below => baseDepth - 1,
            CharacterLayerMode.Above => baseDepth + 1,
            _ => baseDepth,
        };
    }
}
