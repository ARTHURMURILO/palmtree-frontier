using Content.Shared.Atmos.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Electrocution;
using Content.Shared.Maps;
using Content.Shared.SubFloor;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.Construction;

/// <summary>
///     Shared footprint validation for flatpacks that spawn multi-tile machines. The server uses it to
///     veto unpacking; the client placement ghost uses it to colour the preview.
/// </summary>
public sealed class SharedFlatpackPlacementSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private readonly HashSet<EntityUid> _found = new();

    /// <summary>
    ///     Tiles covered by the flatpack once it unpacks at <paramref name="position"/>.
    /// </summary>
    public List<Vector2i> GetFootprintTiles(Vector2i position, FlatpackFootprintComponent footprint, Direction rotation)
    {
        var tiles = new List<Vector2i>(footprint.Footprint.X * footprint.Footprint.Y);
        for (var x = 0; x < footprint.Footprint.X; x++)
        {
            for (var y = 0; y < footprint.Footprint.Y; y++)
            {
                var offset = new Vector2i(footprint.Offset.X + x, footprint.Offset.Y + y);
                tiles.Add(position + RotateOffset(offset, rotation));
            }
        }

        return tiles;
    }

    /// <summary>
    ///     Returns whether the flatpack's footprint fits at <paramref name="position"/>. When it does
    ///     not, <paramref name="reason"/> is a localisation key describing the first problem found.
    /// </summary>
    public bool CanPlace(Entity<FlatpackFootprintComponent> flatpack, EntityUid gridUid, MapGridComponent grid, Vector2i position, out string? reason)
    {
        reason = null;

        var rotation = Transform(flatpack).LocalRotation.GetCardinalDir();
        foreach (var tilePos in GetFootprintTiles(position, flatpack.Comp, rotation))
        {
            if (!_map.TryGetTileRef(gridUid, grid, tilePos, out var tileRef) || tileRef.Tile.IsEmpty)
            {
                reason = "flatpack-unpack-no-floor";
                return false;
            }

            foreach (var anchored in _map.GetAnchoredEntities((gridUid, grid), tilePos))
            {
                // Cables are expected under a machine: they are insulated and can be reached from
                // the edges of the footprint.
                if (HasComp<SubFloorHideComponent>(anchored) && HasComp<ElectrifiedComponent>(anchored))
                    continue;

                // Pipes on a lower layer do not connect to the machine's ports and stay reachable,
                // but a primary-layer pipe would be capped by the machine and cannot be removed.
                if (TryComp<AtmosPipeLayersComponent>(anchored, out var layers) &&
                    layers.CurrentPipeLayer != AtmosPipeLayer.Primary)
                {
                    continue;
                }

                reason = TryComp<AtmosPipeLayersComponent>(anchored, out _)
                    ? "flatpack-unpack-pipe"
                    : "flatpack-unpack-blocked";
                return false;
            }

            if (HasDraggableObstacle(tileRef, flatpack))
            {
                reason = "flatpack-unpack-obstructed";
                return false;
            }
        }

        return true;
    }

    private bool HasDraggableObstacle(TileRef tile, EntityUid ignore)
    {
        _found.Clear();
        _lookup.GetEntitiesInTile(tile, _found, LookupFlags.Dynamic | LookupFlags.Sundries);

        foreach (var other in _found)
        {
            if (other == ignore || Transform(other).Anchored)
                continue;

            if (HasComp<AnchorableComponent>(other))
                return true;
        }

        return false;
    }

    private static Vector2i RotateOffset(Vector2i offset, Direction rotation)
    {
        // Angle zero is south, so south leaves the offsets alone. This matches the rotation the
        // flatpack hands to the machine it spawns.
        return rotation switch
        {
            Direction.East => new Vector2i(-offset.Y, offset.X),
            Direction.North => new Vector2i(-offset.X, -offset.Y),
            Direction.West => new Vector2i(offset.Y, -offset.X),
            _ => offset,
        };
    }
}
