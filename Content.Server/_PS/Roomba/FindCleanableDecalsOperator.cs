using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Decals;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Decals;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._PS.Roomba;

/// <summary>
/// Palmtree: finds the closest cleanable decal (cum drips, grime, crayon scribbles, ...) near the NPC
/// and stores its coordinates on the blackboard so a move-to task can drive the roomba over and a
/// <see cref="CleanDecalsOperator"/> can scrub it.
/// </summary>
/// <remarks>
/// Decals are not entities, so this cannot go through the regular utility query system; instead the
/// whole decal grid is searched in a bounds around the NPC. Only decals with the
/// <see cref="Decal.Cleanable"/> flag qualify, which matches what a space cleaner would remove.
/// </remarks>
public sealed partial class FindCleanableDecalsOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    private DecalSystem _decals = default!;
    private SharedMapSystem _map = default!;

    /// <summary>
    /// How far away (in tiles) the roomba looks for dirty decals.
    /// </summary>
    [DataField]
    public float Radius = 7f;

    /// <summary>
    /// Blackboard key the target coordinates get written to.
    /// </summary>
    [DataField]
    public string KeyCoordinates = "TargetCoordinates";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _decals = sysManager.GetEntitySystem<DecalSystem>();
        _map = sysManager.GetEntitySystem<SharedMapSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent(owner, out TransformComponent? xform) ||
            xform.GridUid is not { } gridUid ||
            !_entManager.TryGetComponent(gridUid, out MapGridComponent? grid) ||
            !_entManager.TryGetComponent(gridUid, out DecalGridComponent? decalGrid))
        {
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        }

        // Scan in grid-local space; decal coordinates are grid-local too.
        var local = _map.GridTileToLocal(gridUid, grid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates)).Position;
        var bounds = new Box2(local - new Vector2(Radius), local + new Vector2(Radius));
        var decals = _decals.GetDecalsIntersecting(gridUid, bounds, decalGrid);

        var found = false;
        var bestDistance = float.MaxValue;
        var bestPosition = Vector2.Zero;

        foreach (var (_, decal) in decals)
        {
            if (!decal.Cleanable)
                continue;

            // Decal coordinates are the bottom-left corner of the sprite; measure to its center.
            var decalCenter = decal.Coordinates + new Vector2(0.5f, 0.5f);
            var distance = (decalCenter - local).LengthSquared();

            if (distance >= bestDistance)
                continue;

            found = true;
            bestDistance = distance;
            bestPosition = decalCenter;
        }

        if (!found)
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));

        var effects = new Dictionary<string, object>
        {
            { KeyCoordinates, new EntityCoordinates(gridUid, bestPosition) },
        };

        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, effects));
    }
}
