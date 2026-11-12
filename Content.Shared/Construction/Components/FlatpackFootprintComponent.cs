using Robust.Shared.GameStates;

namespace Content.Shared.Construction.Components;

/// <summary>
///     Marks a flatpack whose machine occupies more than one tile. The footprint is validated before
///     the flatpack unfolds, so a large machine cannot spawn inside walls, pipes or other machines.
///     The placement ghost reads the same data.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FlatpackFootprintComponent : Component
{
    /// <summary>
    ///     Size of the footprint in tiles, before rotation.
    /// </summary>
    [DataField]
    public Vector2i Footprint = Vector2i.One;

    /// <summary>
    ///     Offset of the footprint's lowest coordinate corner from the flatpack's tile.
    /// </summary>
    [DataField]
    public Vector2i Offset = Vector2i.Zero;
}
