using Content.Server.Decals;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Decals;
using Content.Shared.FixedPoint;
using Content.Shared.Timing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._PS.Roomba;

/// <summary>
/// Palmtree: scrubs every cleanable decal around the target coordinates set by
/// <see cref="FindCleanableDecalsOperator"/> (cum drips, grime, crayon doodles, ...).
/// </summary>
/// <remarks>
/// This is the decal counterpart to the cleanbot's puddle mopping: same water tank and use delay,
/// but the roomba rinses the floor instead of wringing out a puddle. The cleanup radius is generous
/// so a cluster of drips is wiped in one pass, mirroring the space cleaner's tile reaction.
/// </remarks>
public sealed partial class CleanDecalsOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    private DecalSystem _decals = default!;
    private SharedAudioSystem _audio = default!;
    private SharedSolutionContainerSystem _solutions = default!;
    private SharedTransformSystem _transform = default!;
    private UseDelaySystem _useDelay = default!;

    /// <summary>
    /// Blackboard key holding the coordinates to clean.
    /// </summary>
    [DataField]
    public string KeyCoordinates = "TargetCoordinates";

    /// <summary>
    /// Cleanable decals whose center is within this distance of the target coordinates get removed.
    /// </summary>
    [DataField]
    public float Range = 1f;

    /// <summary>
    /// Maximum distance the NPC may be from the target before it starts cleaning.
    /// </summary>
    [DataField]
    public float MaxDistance = 1.5f;

    /// <summary>
    /// Water drained from the absorbent solution per decal removed.
    /// </summary>
    [DataField]
    public FixedPoint2 WaterPerDecal = FixedPoint2.New(0.5f);

    /// <summary>
    /// Solution the bot drains water from.
    /// </summary>
    [DataField]
    public string SolutionName = "absorbed";

    /// <summary>
    /// Sound played while scrubbing.
    /// </summary>
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Effects/Fluids/vacuum-cleaner-fast.ogg",
        AudioParams.Default.WithVariation(0.25f));

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _decals = sysManager.GetEntitySystem<DecalSystem>();
        _audio = sysManager.GetEntitySystem<SharedAudioSystem>();
        _solutions = sysManager.GetEntitySystem<SharedSolutionContainerSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _useDelay = sysManager.GetEntitySystem<UseDelaySystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityCoordinates>(KeyCoordinates, out var coordinates, _entManager) ||
            !coordinates.IsValid(_entManager) ||
            !_entManager.TryGetComponent(owner, out TransformComponent? xform))
        {
            return HTNOperatorStatus.Failed;
        }

        // Keep idling until the move-to task actually dropped us on the spot.
        if (!_transform.InRange(xform.Coordinates, coordinates, MaxDistance))
            return HTNOperatorStatus.Continuing;

        // Share the bot's use delay with the puddle mopping so scrubbing has a cadence.
        if (_entManager.TryGetComponent(owner, out UseDelayComponent? useDelay) &&
            _useDelay.IsDelayed((owner, useDelay)))
        {
            return HTNOperatorStatus.Continuing;
        }

        var gridUid = _transform.GetGrid(coordinates);

        if (gridUid is not { } grid ||
            !_entManager.TryGetComponent(grid, out DecalGridComponent? decalGrid))
        {
            return HTNOperatorStatus.Failed;
        }

        var decals = _decals.GetDecalsInRange(grid, coordinates.Position, Range, static decal => decal.Cleanable);

        // Someone else mopped it first; drop the target instead of camping the spot.
        if (decals.Count == 0)
        {
            blackboard.Remove<EntityCoordinates>(KeyCoordinates);
            return HTNOperatorStatus.Finished;
        }

        ConsumeWater(owner, decals.Count);

        foreach (var (index, _) in decals)
        {
            _decals.RemoveDecal(grid, index, decalGrid);
        }

        _audio.PlayPvs(Sound, owner);

        if (useDelay != null)
            _useDelay.TryResetDelay((owner, useDelay));

        blackboard.Remove<EntityCoordinates>(KeyCoordinates);
        return HTNOperatorStatus.Finished;
    }

    /// <summary>
    /// Drains the bot's water tank for a scrub. Missing water is not fatal: the cleanbot's solution
    /// regenerates on its own, and running dry should not stop the floor from being cleaned.
    /// </summary>
    private void ConsumeWater(EntityUid owner, int decals)
    {
        if (!_solutions.TryGetSolution(owner, SolutionName, out var solutionEnt, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
        {
            return;
        }

        _solutions.RemoveReagent(solutionEnt.Value, "Water", WaterPerDecal * decals);
    }
}
