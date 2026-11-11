using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.Atmos.Components;

/// <summary>
/// Palmtree: one half of a pair of heat exchangers. Two of them have to be placed facing each
/// other; together they equalise the temperature of the two pipelines they are plumbed into
/// without moving any gas between them.
/// Ported from /tg/station's <c>/obj/machinery/atmospherics/components/unary/heat_exchanger</c>.
/// </summary>
/// <remarks>
/// The device keeps /tg/station's unary shape: one pipe node on its back and a flat face on the
/// front. The faces of the two devices have to touch, which puts their pipe nodes back to back so
/// the two pipelines stay separate.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class GasHeatExchangerComponent : Component
{
    /// <summary>
    /// Name of the pipe node on the back of the device.
    /// </summary>
    [DataField]
    public string PipeNodeName = "pipe";

    /// <summary>
    /// Minimum temperature difference (in kelvin) before any heat is exchanged.
    /// </summary>
    [DataField]
    public float MinimumTemperatureDelta = Atmospherics.MinimumTemperatureDeltaToConsider;
}
