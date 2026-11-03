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

    /// <summary>
    /// Palmtree: fixed relay draw per shield generator while it is maintaining the shields, in kW.
    /// </summary>
    public static readonly CVarDef<float> ShipCombatShieldMaintenancePower =
        CVarDef.Create("ps.ship_combat.shield_maintenance_power_kw", 10f, CVar.SERVER);

    /// <summary>
    /// Palmtree: relay draw per shield hit point restored by a regeneration pulse, in kW.
    /// </summary>
    public static readonly CVarDef<float> ShipCombatShieldRegenPowerPerHp =
        CVarDef.Create("ps.ship_combat.shield_regen_power_per_hp_kw", 0.6f, CVar.SERVER);

    /// <summary>
    /// Palmtree: transient relay draw added by each bolt shot, in kW.
    /// </summary>
    public static readonly CVarDef<float> ShipCombatWeaponBoltPower =
        CVarDef.Create("ps.ship_combat.weapon_bolt_power_kw", 2f, CVar.SERVER);

    /// <summary>
    /// Palmtree: transient relay draw added by each beam shot, in kW.
    /// </summary>
    public static readonly CVarDef<float> ShipCombatWeaponBeamPower =
        CVarDef.Create("ps.ship_combat.weapon_beam_power_kw", 4f, CVar.SERVER);

    /// <summary>
    /// Palmtree: transient relay draw added by each torpedo launch, in kW.
    /// </summary>
    public static readonly CVarDef<float> ShipCombatTorpedoLaunchPower =
        CVarDef.Create("ps.ship_combat.torpedo_launch_power_kw", 20f, CVar.SERVER);

    /// <summary>
    /// Palmtree: how long a weapon/torpedo transient relay draw lasts, in seconds.
    /// </summary>
    public static readonly CVarDef<float> ShipCombatActionPowerDuration =
        CVarDef.Create("ps.ship_combat.action_power_duration_seconds", 1f, CVar.SERVER);

    /// <summary>
    /// Palmtree: global torpedo turn rate in degrees per second. 0 (default) uses each torpedo
    /// prototype's own turn rate.
    /// </summary>
    public static readonly CVarDef<float> TorpedoTurnRate =
        CVarDef.Create("ps.ship_combat.torpedo_turn_rate", 0f, CVar.SERVER);

    /// <summary>
    /// Palmtree: how long a torpedo keeps steering before it gives up and flies straight, in seconds.
    /// Zero disables the limit.
    /// </summary>
    public static readonly CVarDef<float> TorpedoSteeringDuration =
        CVarDef.Create("ps.ship_combat.torpedo_steering_duration", 120f, CVar.SERVER);

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
