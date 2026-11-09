using Content.Shared.Maps;
using Content.Shared.Physics;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared.Construction.Conditions;

[UsedImplicitly]
[DataDefinition]
public sealed partial class TileNotBlocked : IConstructionCondition
{
    [DataField("filterMobs")] private bool _filterMobs = false;
    [DataField("failIfSpace")] private bool _failIfSpace = true;
    [DataField("failIfNotSturdy")] private bool _failIfNotSturdy = true;

    /// <summary>
    ///     Palmtree: by default directional windows do not count as tile-filling obstacles, so furniture,
    ///     machines and pipes can share their tile. Full-tile and full-height builds (walls, doors,
    ///     airlocks, windoors) opt in with <c>blockDirectionalWindows: true</c>.
    /// </summary>
    [DataField("blockDirectionalWindows")] private bool _blockDirectionalWindows = false;

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        if (!IoCManager.Resolve<IEntityManager>().TrySystem<TurfSystem>(out var turfSystem))
            return false;

        if (!turfSystem.TryGetTileRef(location, out var tileRef))
        {
            return false;
        }

        if (turfSystem.IsSpace(tileRef.Value) && _failIfSpace)
        {
            return false;
        }

        if (!turfSystem.GetContentTileDefinition(tileRef.Value).Sturdy && _failIfNotSturdy)
        {
            return false;
        }

        // Palmtree: `includeConstructionBlockers` lets a soft machine footprint reserve its tiles.
        // Directional windows are ignored unless this particular build needs the whole tile.
        return !turfSystem.IsTileBlocked(tileRef.Value,
            _filterMobs ? CollisionGroup.MobMask : CollisionGroup.Impassable,
            includeConstructionBlockers: true,
            ignoreDirectionalWindows: !_blockDirectionalWindows);
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "construction-step-condition-tile-not-blocked",
        };
    }
}
