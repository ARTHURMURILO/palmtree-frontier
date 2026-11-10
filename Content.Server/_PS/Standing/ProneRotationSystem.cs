using Content.Shared.Buckle.Components;
using Content.Shared.Humanoid;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server._PS.Standing;

/// <summary>
/// Palmtree: flips downed/crawling humanoids so they lie with their head toward the direction they
/// are moving. Standing sprites are upright; this only drives the horizontal rotation used while
/// prone. North/south movement keeps the last left/right lie, and buckled mobs are left to the bed
/// strap rotation.
/// </summary>
public sealed class ProneRotationSystem : EntitySystem
{
    [Dependency] private readonly SharedRotationVisualsSystem _rotation = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StandingStateComponent, DownedEvent>(OnDowned);
        SubscribeLocalEvent<StandingStateComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<BuckleComponent, UnbuckledEvent>(OnUnbuckled);
    }

    private void OnDowned(Entity<StandingStateComponent> ent, ref DownedEvent args)
    {
        UpdateProneRotation(ent);
    }

    private void OnMove(Entity<StandingStateComponent> ent, ref MoveEvent args)
    {
        UpdateProneRotation(ent);
    }

    private void OnUnbuckled(Entity<BuckleComponent> ent, ref UnbuckledEvent args)
    {
        if (TryComp<StandingStateComponent>(ent.Owner, out var standing))
            UpdateProneRotation((ent.Owner, standing));
    }

    private void UpdateProneRotation(Entity<StandingStateComponent> ent)
    {
        if (ent.Comp.Standing || !HasComp<HumanoidAppearanceComponent>(ent.Owner))
            return;

        // Beds/straps own the lying angle while buckled.
        if (TryComp<BuckleComponent>(ent.Owner, out var buckle) && buckle.Buckled)
            return;

        if (!TryComp<RotationVisualsComponent>(ent.Owner, out var visuals))
            return;

        var direction = _transform.GetWorldRotation(ent.Owner).GetDir();
        Angle target;
        switch (direction)
        {
            case Direction.East:
            case Direction.NorthEast:
            case Direction.SouthEast:
                // 90 degrees is the existing head-left lie; 270 mirrors it to the right.
                target = Angle.FromDegrees(270);
                break;
            case Direction.West:
            case Direction.NorthWest:
            case Direction.SouthWest:
                target = Angle.FromDegrees(90);
                break;
            default:
                // Moving north/south keeps the last left/right lie. Repair a bed/other angle so the
                // mob cannot remain rotated in a direction that is not a valid prone side.
                if (visuals.HorizontalRotation.Equals(Angle.FromDegrees(90))
                    || visuals.HorizontalRotation.Equals(Angle.FromDegrees(270)))
                {
                    return;
                }

                target = visuals.DefaultRotation;
                break;
        }

        _rotation.SetHorizontalAngle((ent.Owner, visuals), target);
    }
}
