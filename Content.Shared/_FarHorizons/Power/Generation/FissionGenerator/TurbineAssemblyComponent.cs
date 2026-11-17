using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
///     Lets a gas turbine be packed back into its flatpack. Also records the site works a flatpack
///     laid around the turbine so packing it up can remove exactly those and restore the deck.
/// </summary>
[RegisterComponent]
public sealed partial class TurbineAssemblyComponent : Component
{
    /// <summary>
    ///     Flatpack prototype spawned when the turbine is packed up.
    /// </summary>
    [DataField]
    public EntProtoId Flatpack = "GasTurbineFlatpack";

    /// <summary>
    ///     How long packing the turbine up takes.
    /// </summary>
    [DataField]
    public TimeSpan PackDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Floor tile to lay under the whole footprint on unpack. Null leaves the deck alone.
    /// </summary>
    [DataField]
    public string? Floor = "FloorReinforced";

    /// <summary>
    ///     Whether the flatpack lays an HV cable on every footprint tile.
    /// </summary>
    [DataField]
    public bool CableOnFootprint = true;

    /// <summary>
    ///     Whether the turbine was deployed by a flatpack. Map-spawned turbines do not get the
    ///     pack-up verb, so they cannot mint free flatpacks. Saved so a deployed turbine stays
    ///     packable after the map is reloaded.
    /// </summary>
    [DataField]
    public bool Deployed;

    /// <summary>
    ///     Tiles the flatpack laid an HV cable on, removed again when packing up. Stored as tiles
    ///     rather than entity UIDs so the site works survive a map save/load.
    /// </summary>
    [DataField]
    public List<Vector2i> CableTiles = new();

    /// <summary>
    ///     Tiles replaced by the flatpack, with the floor that was there before.
    /// </summary>
    [DataField]
    public List<TurbineFloorRestore> Floors = new();

    /// <summary>
    ///     The floor tile the flatpack laid down, used to avoid restoring a deck the crew has since changed.
    /// </summary>
    [DataField]
    public int FloorTile;
}

[DataDefinition]
public sealed partial class TurbineFloorRestore
{
    [DataField]
    public Vector2i Position;

    [DataField]
    public int OriginalTileType;
}
