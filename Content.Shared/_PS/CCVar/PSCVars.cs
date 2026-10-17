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
}
