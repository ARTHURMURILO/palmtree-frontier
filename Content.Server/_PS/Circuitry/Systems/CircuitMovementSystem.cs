using System.Numerics;
using Content.Server._PS.Circuitry.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._PS.Circuitry.Systems;

public enum CircuitWalkResult : byte
{
    Failed,
    NoPower,
    Started,
    AlreadyThere,
    AlreadyPathing,
}

/// <summary>
/// Palmtree: drives assemblies. Direct movement writes the same <see cref="InputMoverComponent"/>
/// fields NPC steering uses; path movement delegates to <see cref="NPCSteeringSystem"/>.
/// </summary>
public sealed class CircuitMovementSystem : EntitySystem
{
    /// <summary>
    /// While already pathing, ignore new walk_to targets this close to the current one. Scripts
    /// that chase a moving entity call walk_to every tick; without this guard each call cancels
    /// the in-flight path and starts another pathfinding job.
    /// </summary>
    private const float RetargetDistance = 0.75f;

    [Dependency] private readonly CircuitAssemblySystem _assembly = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CircuitAssemblyComponent, InputMoverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var component, out var mover, out var xform))
        {
            var moved = (xform.LocalPosition - component.LastMovePosition).Length();
            component.LastMovePosition = xform.LocalPosition;

            if (moved > 0.001f && component.MovePowerPerMeter > 0f)
            {
                component.MovementPowerDebt += moved * component.MovePowerPerMeter;

                if (component.MovementPowerDebt >= 1f)
                {
                    var cost = MathF.Floor(component.MovementPowerDebt);
                    component.MovementPowerDebt -= cost;

                    if (!_assembly.TryConsumePower(uid, cost, component))
                    {
                        _assembly.StopMovement(uid, component);
                        continue;
                    }
                }
            }

            // Stop pathing as soon as we are inside the arrival tolerance, even if the script does
            // not poll walk_status(). NPC steering can otherwise keep nudging around a small target.
            if (component.WalkMode == CircuitWalkMode.Path &&
                component.WalkTarget is { } target &&
                (xform.Coordinates.Position - target.Position).Length() <= component.WalkArrivalRange)
            {
                _assembly.StopMovement(uid, component);
                component.WalkArrived = true;
                continue;
            }

            if (component.WalkMode != CircuitWalkMode.Direct)
                continue;

            if (!component.Powered || component.MoveDirection == Vector2.Zero || !mover.CanMove)
            {
                mover.CurTickSprintMovement = Vector2.Zero;
                continue;
            }

            mover.CurTickSprintMovement = component.MoveDirection;
            mover.LastInputTick = _timing.CurTick;
            mover.LastInputSubTick = ushort.MaxValue;
            var moveEvent = new SpriteMoveEvent(true);
            RaiseLocalEvent(uid, ref moveEvent);
        }
    }

    public bool TryStartDirect(EntityUid uid, Vector2 direction, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        if (!component.Capabilities.HasFlag(CircuitCapability.Locomotion) || !component.Powered)
            return false;

        if (direction.LengthSquared() < 0.0001f)
        {
            _assembly.StopMovement(uid, component);
            return true;
        }

        direction = direction.Normalized();
        component.WalkMode = CircuitWalkMode.Direct;
        component.WalkArrived = false;
        component.MoveDirection = direction;
        component.WalkTarget = null;
        component.LastMovePosition = Transform(uid).LocalPosition;

        // A direct command overrides any in-progress path.
        _steering.Unregister(uid);
        return true;
    }

    public CircuitWalkResult TryWalkTo(
        EntityUid uid,
        EntityCoordinates target,
        float range = 0.5f,
        bool smooth = true,
        CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return CircuitWalkResult.Failed;

        if (!component.Capabilities.HasFlag(CircuitCapability.Locomotion))
            return CircuitWalkResult.Failed;

        if (!component.Powered)
            return CircuitWalkResult.NoPower;

        // Pathfinding rarely lands exactly on an item's centre. At speed, a fixed 0.5 tile
        // tolerance is not enough braking distance, so scale the arrival target with walk speed.
        // Passing smooth=false keeps the older fixed-tolerance behavior for scripts that need it.
        range = MathF.Max(0.5f, range);

        var walkSpeed = 2.5f;
        if (TryComp<MovementSpeedModifierComponent>(uid, out var movementSpeed))
            walkSpeed = MathF.Max(0.1f, movementSpeed.CurrentWalkSpeed);

        if (smooth)
            range = MathF.Max(range, Math.Clamp(walkSpeed * 0.25f, 0.5f, 1.25f));

        // Already close enough: do not start a new path just to shuffle around the target.
        if ((Transform(uid).Coordinates.Position - target.Position).Length() <= range)
        {
            _assembly.StopMovement(uid, component);
            component.WalkArrived = true;
            return CircuitWalkResult.AlreadyThere;
        }

        // The script is chasing a target that has barely moved since the last command. Keep the
        // existing path instead of cancelling it and allocating another pathfinding request.
        if (component.WalkMode == CircuitWalkMode.Path &&
            component.WalkTarget is { } currentTarget &&
            (currentTarget.Position - target.Position).LengthSquared() <=
            RetargetDistance * RetargetDistance)
        {
            if (!TryComp<NPCSteeringComponent>(uid, out var existingSteering) ||
                existingSteering.Status != SteeringStatus.NoPath)
            {
                return CircuitWalkResult.AlreadyPathing;
            }
        }

        if (!_assembly.TryConsumePower(uid, component.ActionPowerCost, component))
            return CircuitWalkResult.NoPower;

        EnsureComp<ActiveNPCComponent>(uid);
        var steering = _steering.Register(uid, target);
        steering.DirectMove = false;
        steering.Range = range;
        steering.InRangeMaxSpeed = smooth ? MathF.Max(0.1f, walkSpeed * 0.15f) : null;

        // A new explicit walk command must clear a previous terminal pathfinding failure. Without
        // this, re-issuing walk_to to the same coordinates after a door opens (or a temporary
        // blockage clears) returns the existing NoPath steering component and never retries.
        if (steering.Status == SteeringStatus.NoPath)
        {
            steering.Status = SteeringStatus.Moving;
            steering.FailedPathCount = 0;
            steering.CurrentPath.Clear();
            steering.PathfindToken?.Cancel();
            steering.PathfindToken = null;
        }

        component.WalkMode = CircuitWalkMode.Path;
        component.WalkArrived = false;
        component.MoveDirection = Vector2.Zero;
        component.WalkTarget = target;
        component.WalkArrivalRange = range;
        component.LastMovePosition = Transform(uid).LocalPosition;
        return CircuitWalkResult.Started;
    }

    public void Stop(EntityUid uid, CircuitAssemblyComponent? component = null)
    {
        _assembly.StopMovement(uid, component);
    }

    public string GetWalkStatus(EntityUid uid, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return "idle";

        if (component.WalkMode == CircuitWalkMode.Direct)
            return "moving";

        if (component.WalkMode != CircuitWalkMode.Path)
            return component.WalkArrived ? "arrived" : "idle";

        // Do not rely solely on NPC steering's idea of "in range": small targets and grid
        // alignment can make it keep nudging past the point forever.
        if (component.WalkTarget is { } target &&
            (Transform(uid).Coordinates.Position - target.Position).Length() <=
            MathF.Max(0.5f, component.WalkArrivalRange))
        {
            _assembly.StopMovement(uid, component);
            component.WalkArrived = true;
            return "arrived";
        }

        if (!TryComp<NPCSteeringComponent>(uid, out var steering))
            return "idle";

        if (steering.Status == SteeringStatus.InRange)
        {
            _assembly.StopMovement(uid, component);
            component.WalkArrived = true;
            return "arrived";
        }

        return steering.Status == SteeringStatus.NoPath ? "no_path" : "moving";
    }
}
