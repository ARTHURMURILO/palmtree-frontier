using Robust.Shared.Configuration;

namespace Content.Shared._PS.CCVar;

[CVarDefs]
public sealed partial class PSCVars
{
    /*
     *  Placement
     */

    /// <summary>
    /// Palmtree: extra X offset, in tiles, applied by the "SnapgridCenterOffset" placement mode so
    /// admins and mappers can nudge spawned entities slightly off tile centers. Zero disables it.
    /// </summary>
    public static readonly CVarDef<float> PlacementOffsetX =
        CVarDef.Create("ps.placement.offset_x", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Palmtree: extra Y offset, in tiles, applied by the "SnapgridCenterOffset" placement mode.
    /// </summary>
    public static readonly CVarDef<float> PlacementOffsetY =
        CVarDef.Create("ps.placement.offset_y", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /*
     *  Ship combat
     */

    /// <summary>
    /// Palmtree: when false (default) ship turrets hold fire while the ship's own hull blocks the
    /// firing line - the turret pops "Prevented from firing" and the tactical console's status
    /// shows a warning naming nothing, the shipcombat log names the blocker. Set it to true to let
    /// turrets shoot through their own grid again.
    /// </summary>
    public static readonly CVarDef<bool> ShipCombatTurretsShootThroughHull =
        CVarDef.Create("ps.ship_combat.turrets_shoot_through_hull", false, CVar.SERVER | CVar.REPLICATED);

    /*
     *  Client
     */

    /// <summary>
    /// Palmtree: client-side tile overlays (ambient occlusion, roofs) skip views larger than this
    /// area, in tiles squared. The engine's tile enumerator recurses once per skipped tile and
    /// overflows the stack on very large views; raise this if you want the overlays on huge views.
    /// </summary>
    public static readonly CVarDef<float> TileOverlayAreaLimit =
        CVarDef.Create("ps.client.tile_overlay_area_limit", 4096f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
