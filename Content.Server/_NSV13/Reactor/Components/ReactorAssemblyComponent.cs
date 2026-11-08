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
    [ViewVariables]
    public List<EntityUid> Parts = new();

    /// <summary>
    ///     Tiles replaced by the flatpack, with the tiles that were there before.
    /// </summary>
    [ViewVariables]
    public List<(Vector2i Position, Tile Original)> Floors = new();

    /// <summary>
    ///     The tile the flatpack laid down, used to avoid restoring a deck the crew has since changed.
    /// </summary>
    [ViewVariables]
    public int FloorTile;
}
