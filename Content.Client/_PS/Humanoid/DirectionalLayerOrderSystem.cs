using Content.Client.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._PS.Humanoid;

/// <summary>
/// Palmtree: some humanoid layers are ordered differently depending on the direction the sprite is
/// drawn in on the local client (south-facing genitals above hair/tails, side-facing tails under
/// hair, side-facing snouts above hair). Sprite directions are derived from world rotation plus the
/// camera rotation and raise no event, so this system watches for cardinal changes and rebuilds the
/// humanoid sprite, which re-applies the direction-aware marking layers in
/// <see cref="HumanoidAppearanceSystem"/>.
/// </summary>
public sealed class DirectionalLayerOrderSystem : EntitySystem
{
    [Dependency] private readonly HumanoidAppearanceSystem _appearance = default!;

    private readonly Dictionary<EntityUid, (RsiDirection Direction, bool SideFacing)> _lastDirection = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanoidAppearanceComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, HumanoidAppearanceComponent component, ComponentShutdown args)
    {
        _lastDirection.Remove(uid);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var humanoid, out _))
        {
            var direction = _appearance.GetScreenDirection(uid);
            var sideFacing = _appearance.IsSideFacing(uid);

            if (_lastDirection.TryGetValue(uid, out var last)
                && last == (direction, sideFacing))
            {
                continue;
            }

            _lastDirection[uid] = (direction, sideFacing);

            // Without direction-sensitive markings the direction has no effect on layer order.
            if (!NeedsDirectionalOrder(humanoid))
                continue;

            _appearance.RefreshSprite(uid);
        }
    }

    private static bool NeedsDirectionalOrder(HumanoidAppearanceComponent humanoid)
    {
        return HasMarkings(humanoid, MarkingCategories.Genital)
               || HasMarkings(humanoid, MarkingCategories.Tail)
               || HasMarkings(humanoid, MarkingCategories.TailExtras)
               || (humanoid.SnoutAboveHair
                   && (HasMarkings(humanoid, MarkingCategories.Snout)
                       || HasMarkings(humanoid, MarkingCategories.SnoutCover)));
    }

    private static bool HasMarkings(HumanoidAppearanceComponent humanoid, MarkingCategories category)
    {
        return humanoid.MarkingSet.TryGetCategory(category, out var markings) && markings.Count > 0;
    }
}
