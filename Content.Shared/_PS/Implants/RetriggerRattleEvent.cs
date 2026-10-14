using Robust.Shared.GameObjects;

namespace Content.Shared._PS.Implants;

/// <summary>
/// Palmtree: raised on an implanted mob to re-send its rattle implant message. Used when a body
/// recovered from an expedition arrives back on the main map, so medics get real coordinates
/// instead of the (about to be deleted) expedition map's.
/// </summary>
public sealed class RetriggerRattleEvent : EntityEventArgs
{
    public readonly EntityUid Implanted;

    public RetriggerRattleEvent(EntityUid implanted)
    {
        Implanted = implanted;
    }
}
