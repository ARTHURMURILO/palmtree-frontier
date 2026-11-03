using Content.Shared._PS.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;

namespace Content.Client._PS.Overlays;

/// <summary>
/// Palmtree: guards client-side tile overlays against a stack overflow in the engine's tile
/// enumerator.
/// </summary>
/// <remarks>
/// <c>SharedMapSystem.TilesEnumerator.MoveNext</c> recurses once per skipped (empty or
/// predicate-rejected) tile, so walking a very large view (huge viewport, low render scale or a
/// zoomed out eye) recurses tens of thousands of frames deep and overflows the stack. The overlays
/// that walk view-bounded tiles skip such views instead; the limit is the
/// <c>ps.client.tile_overlay_area_limit</c> cvar, in tiles squared.
/// </remarks>
public static class TileEnumerationGuard
{
    /// <summary>
    /// True when the given view bounds are too large to walk tile by tile safely.
    /// </summary>
    public static bool TooLarge(IConfigurationManager cfg, Box2 bounds)
    {
        return bounds.Width * bounds.Height > cfg.GetCVar(PSCVars.TileOverlayAreaLimit);
    }
}
