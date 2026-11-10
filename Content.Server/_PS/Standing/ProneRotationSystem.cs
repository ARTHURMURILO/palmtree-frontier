using Content.Shared.Buckle.Components;
using Content.Shared.Humanoid;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server._PS.Standing;

/// <summary>
/// Palmtree: sets a downed/crawling humanoid's lying direction from the direction they were facing
/// when they fell. The angle is not updated while prone, so it cannot flip while crawling; standing
/// up clears the lock (the next knockdown picks the new direction). Buckled mobs keep the bed/strap
/// angle.
/// </summary>
public sealed class ProneRotationSystem : EntitySystem
{
    [Dependency] private readonly SharedRotationVisualsSystem _rotation = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StandingStateComponent, DownedEvent>(OnDowned);
    }

    private void OnDowned(Entity<StandingStateComponent> ent, ref DownedEvent args)
    {
        if (ent.Comp.Standing || !HasComp<HumanoidAppearanceComponent>(ent.Owner))
            return;

        // Beds/straps own the lying angle while buckled.
        if (TryComp<BuckleComponent>(ent.Owner, out var buckle) && buckle.Buckled)
            return;

        if (!TryComp<RotationVisualsComponent>(ent.Owner, out var visuals))
            return;

        var direction = _transform.GetWorldRotation(ent.Owner).GetDir();
        var target = direction switch
        {
            Direction.East or Direction.NorthEast or Direction.SouthEast => Angle.FromDegrees(270),
            Direction.West or Direction.NorthWest or Direction.SouthWest => Angle.FromDegrees(90),
            _ => visuals.DefaultRotation,
        };

        _rotation.SetHorizontalAngle((ent.Owner, visuals), target);
    }
}
