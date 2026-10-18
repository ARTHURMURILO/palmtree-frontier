using Content.Shared.Shuttles.Components;

namespace Content.Shared.Shuttles.Systems;

public abstract class SharedRadarConsoleSystem : EntitySystem
{
    public const float DefaultMaxRange = 256f;

    protected virtual void UpdateState(EntityUid uid, RadarConsoleComponent component)
    {
    }

    public void SetRange(EntityUid uid, float value, RadarConsoleComponent component)
    {
        if (component.MaxRange.Equals(value))
            return;

        component.MaxRange = value;
        Dirty(uid, component);
        UpdateState(uid, component);
    }

    // Palmtree: consoles can keep pointing at ships that no longer exist. The bluespace dock deletes
    // stored ships and the shipyard deletes sold ones, and GetNetEntity on a deleted entity logs
    // "Can't resolve MetaDataComponent" on every console state refresh (a docked ship arriving
    // through FTL refreshes them all). These two helpers drop the reference.

    /// <summary>
    /// Drops a console's target when the entity it points at no longer exists.
    /// </summary>
    /// <returns>True when a dead target was cleared.</returns>
    public bool ClearTargetIfDead(EntityUid uid, RadarConsoleComponent component)
    {
        if (component.TargetEntity is not { } target || Exists(target))
            return false;

        component.TargetEntity = null;
        Dirty(uid, component);
        return true;
    }

    /// <summary>
    /// Drops every console target that points at an entity that is about to be deleted.
    /// </summary>
    public void ClearTargetsPointingAt(EntityUid target)
    {
        var query = AllEntityQuery<RadarConsoleComponent>();
        while (query.MoveNext(out var uid, out var radar))
        {
            if (radar.TargetEntity != target)
                continue;

            radar.TargetEntity = null;
            Dirty(uid, radar);
        }
    }
}
