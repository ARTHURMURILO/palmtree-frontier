using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     Added at runtime to a reactor that came out of a flatpack. Holds the parts the unpack laid down so
///     that deconstruction can remove exactly those, restore the deck, and hand the flatpack back.
/// </summary>
[RegisterComponent]
public sealed partial class ReactorAssemblyComponent : Component
{
    /// <summary>
    ///     Flatpack prototype to spawn when the assembly is packed up again.
    /// </summary>
    [DataField]
    public EntProtoId Flatpack = default!;

    /// <summary>
    ///     How long packing the reactor up takes.
    /// </summary>
    [DataField]
    public TimeSpan DeconstructDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Highest core temperature that still allows deconstruction.
    /// </summary>
    [DataField]
    public float ColdLimit = 100f;

    /// <summary>
    ///     Footprint the flatpack deployed over, used to check the site before packing up again.
    /// </summary>
    [DataField]
    public Vector2i Footprint = Vector2i.One;

    /// <summary>
    ///     Entities the flatpack spawned around the reactor (cables, pipe stubs).
    /// </summary>
    /// <remarks>
    ///     Palmtree: serialized. A reactor that is stored and retrieved must still know which cables
    ///     and pipe stubs it laid, or packing it up leaves those stubs behind. EntityUid references
    ///     are remapped by the ship-file loader like any other grid reference; deleted stubs come
    ///     back as invalid/omitted entries and are skipped by <c>Deconstruct</c>.
    /// </remarks>
    [DataField]
    [ViewVariables]
    public List<EntityUid> Parts = new();

    /// <summary>
    ///     Tiles replaced by the flatpack, with the tiles that were there before.
    /// </summary>
    /// <remarks>
    ///     Palmtree: serialized. <see cref="Tile"/> has no data definition, so the original tile is
    ///     stored as its stable tile-prototype ID (plus the variant bytes) instead of a raw
    ///     <see cref="Tile"/>. Without this a retrieved reactor packed up without restoring the deck.
    /// </remarks>
    [DataField]
    [ViewVariables]
    public List<ReactorFloorPatch> Floors = new();

    /// <summary>
    ///     The tile the flatpack laid down, used to avoid restoring a deck the crew has since changed.
    /// </summary>
    [DataField]
    [ViewVariables]
    public int FloorTile;
}

/// <summary>
///     One deck tile replaced during a reactor deployment, in a form the ship serializer can write.
/// </summary>
[DataDefinition]
public partial struct ReactorFloorPatch
{
    [DataField]
    public Vector2i Position;

    /// <summary>Tile prototype ID, e.g. <c>Plating</c>; stable across tile-registry changes.</summary>
    [DataField]
    public string OriginalTile = string.Empty;

    [DataField]
    public byte Flags;

    [DataField]
    public byte Variant;

    [DataField]
    public byte RotationMirroring;

    public ReactorFloorPatch(Vector2i position, Tile original, string originalTile)
    {
        Position = position;
        OriginalTile = originalTile;
        Flags = original.Flags;
        Variant = original.Variant;
        RotationMirroring = original.RotationMirroring;
    }

    public Tile ToTile(int tileId)
    {
        return new Tile(tileId, Flags, Variant, RotationMirroring);
    }
}
