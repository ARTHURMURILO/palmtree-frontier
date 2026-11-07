using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     Marks a flatpack as a reactor deployment: the flatpack itself carries the site works that have to
///     appear around the unit (floor, cabling, pipe stubs), because they have to be checked before spawn.
/// </summary>
[RegisterComponent]
public sealed partial class ReactorFlatpackComponent : Component
{
    /// <summary>
    ///     Footprint in tiles, centred on the flatpack's tile.
    /// </summary>
    [DataField]
    public Vector2i Footprint = Vector2i.One;

    /// <summary>
    ///     Floor tile to lay under the whole footprint. Null leaves the deck alone.
    /// </summary>
    [DataField]
    public string? Floor;

    /// <summary>
    ///     Whether to lay an HV cable on every footprint tile.
    /// </summary>
    [DataField]
    public bool CableOnFootprint;

    /// <summary>
    ///     Whether to put a straight pipe on the tile each of the reactor's pipe ports faces.
    /// </summary>
    [DataField]
    public bool PipesOnPorts;

    /// <summary>
    ///     How long deconstruction takes once the core is cold and off.
    /// </summary>
    [DataField]
    public TimeSpan DeconstructDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Highest core temperature that still allows deconstruction.
    /// </summary>
    [DataField]
    public float ColdLimit = 100f;
}
