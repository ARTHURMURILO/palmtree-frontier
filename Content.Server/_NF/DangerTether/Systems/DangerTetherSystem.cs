using Content.Server.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._NF.DangerTether;

/// <summary>
/// A system to handle tethering dangerous objects, and deleting them when out of range of any tether.
/// Runs periodic checks to handle deletion.
/// </summary>
/// <remarks>
///     Palmtree: a tether is not the only place a dangerous object is allowed to exist. Anything
///     sitting on a ship's grid is somebody's deliberate engineering, so it is left alone;
///     see <see cref="AnyTetherInRange"/>.
/// </remarks>
public sealed class DangerTetherSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TransformSystem _transform = default!;

    private readonly TimeSpan _scanPeriod = TimeSpan.FromSeconds(0.5);
    private TimeSpan _nextScan = TimeSpan.Zero;
    private List<(MapCoordinates Position, float Distance)> _tethers = new();
    private EntityQuery<ShuttleComponent> _shuttleQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DangerTetheredComponent, MapInitEvent>(OnTetheredMapInit);
        _shuttleQuery = GetEntityQuery<ShuttleComponent>();
    }

    /// <summary>
    /// Update: periodically, check that all DangerTethered entities are in range of a tether.
    /// If they aren't, delete them.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextScan)
            return;

        _nextScan += _scanPeriod;

        PopulateTetherList();

        var tetheredQuery = EntityQueryEnumerator<DangerTetheredComponent>();
        while (tetheredQuery.MoveNext(out var targetUid, out _))
        {
            if (!AnyTetherInRange(targetUid))
                QueueDel(targetUid);
        }
    }

    /// <summary>
    /// DangerTethered MapInit: must be in range of a tether, otherwise delete it.
    /// </summary>
    private void OnTetheredMapInit(Entity<DangerTetheredComponent> ent, ref MapInitEvent args)
    {
        PopulateTetherList();
        if (!AnyTetherInRange(ent))
            QueueDel(ent);
    }

    private void PopulateTetherList()
    {
        _tethers.Clear();
        var tetherQuery = EntityQueryEnumerator<DangerTetherComponent>();
        while (tetherQuery.MoveNext(out var tetherUid, out var tether))
        {
            _tethers.Add((_transform.GetMapCoordinates(tetherUid), tether.MaxDistance));
        }
    }

    public bool AnyTetherInRange(EntityUid ent)
    {
        // Palmtree: a ship is a tether. The Frontier check exists to stop a singularity being spawned
        // in deep space and drifting into a station; it also deleted the singularity or energy ball a
        // supermatter produces when it fails, because a ship has no DangerTether marker to stand in
        // range of. That made the collapse failure mode impossible to reach on the one kind of grid
        // this fork is actually about. Mapped stations and POIs keep their markers (and their
        // protection); a flying grid is somebody's own problem to survive.
        if (TryComp(ent, out TransformComponent? xform) &&
            xform.GridUid is { } grid &&
            _shuttleQuery.HasComp(grid))
        {
            return true;
        }

        var targetCoords = _transform.GetMapCoordinates(ent);
        foreach (var tetherEntry in _tethers)
        {
            if (tetherEntry.Position.MapId != targetCoords.MapId)
                continue;

            if (tetherEntry.Position.InRange(targetCoords, tetherEntry.Distance))
                return true;
        }

        return false;
    }
}
