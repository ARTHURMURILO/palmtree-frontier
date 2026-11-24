using Content.Shared._PS.Construction.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Electrocution;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.SubFloor;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared._PS.Construction;

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
    /// <param name="ignoreMob">
    ///     Mob to ignore in the obstacle check. A player unpacking a large flatpack cannot reach it
    ///     from outside a 3x5 footprint (interaction range is 1.5 m), so their own body must not veto
    ///     the placement; everyone else's still does. The client ghost passes the local player.
    /// </param>
    public bool CanPlace(Entity<FlatpackFootprintComponent> flatpack, EntityUid gridUid, MapGridComponent grid,
        Vector2i position, out string? reason, EntityUid? ignoreMob = null)
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

            if (GetFootprintObstacle(tileRef, flatpack, ignoreMob) is { } obstacle)
            {
                reason = obstacle;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Returns the localisation key of the first loose entity that would end up inside the
    ///     machine after unpacking, or null when the tile is clear.
    /// </summary>
    private string? GetFootprintObstacle(TileRef tile, EntityUid flatpack, EntityUid? ignoreMob)
    {
        _found.Clear();
        _lookup.GetEntitiesInTile(tile, _found, LookupFlags.Dynamic | LookupFlags.Sundries);

        foreach (var other in _found)
        {
            // The flatpack itself is about to be consumed.
            if (other == flatpack)
                continue;

            // The player doing the unpacking has to stand on the footprint to reach it.
            if (other == ignoreMob)
                continue;

            // Anchored machines, pipes and structures are rejected by the anchored-entity pass above.
            if (Transform(other).Anchored)
                continue;

            // Living mobs have no AnchorableComponent, but spawning a machine inside a player or
            // NPC would trap them in a solid body.
            if (HasComp<MobStateComponent>(other))
                return "flatpack-unpack-mob";

            if (HasComp<AnchorableComponent>(other))
                return "flatpack-unpack-obstructed";

            // Any other loose collidable body (crates, lockers, loose machinery) would also be
            // sealed inside the machine once it unpacks, so it has to move first.
            if (TryComp<PhysicsComponent>(other, out var body) && body.CanCollide)
                return "flatpack-unpack-obstructed";
        }

        return null;
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
