using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.Atmos.Components;

/// <summary>
/// Palmtree: a binary atmos device that equalises the temperature of the gas on its two connected
/// pipelines without moving any gas.
/// Ported from /tg/station's <c>/obj/machinery/atmospherics/components/unary/heat_exchanger</c>.
/// </summary>
/// <remarks>
/// /tg/station uses a pair of unary devices that have to be placed facing each other. This port
/// folds that pair into a single binary device: the two pipe nodes are simply two pipelines that
/// are not connected to each other, and heat is exchanged between their mixtures.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class GasHeatExchangerComponent : Component
{
    /// <summary>
    /// Name of the first pipe node.
    /// </summary>
    [DataField]
    public string InletName = "inlet";

    /// <summary>
    /// Name of the second pipe node.
    /// </summary>
    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    /// Minimum temperature difference (in kelvin) before any heat is exchanged.
    /// </summary>
    [DataField]
    public float MinimumTemperatureDelta = Atmospherics.MinimumTemperatureDeltaToConsider;
}
