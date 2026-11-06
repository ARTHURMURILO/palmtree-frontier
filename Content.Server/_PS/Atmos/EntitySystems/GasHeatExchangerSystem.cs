using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._PS.Atmos.Components;
using Content.Shared.Atmos;
using JetBrains.Annotations;

namespace Content.Server._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: runs the pipe-to-pipe heat exchanger. Both pipelines are brought to the heat-capacity
/// weighted equilibrium temperature, without any gas moving between them.
/// </summary>
[UsedImplicitly]
public sealed class GasHeatExchangerSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasHeatExchangerComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnAtmosUpdate(Entity<GasHeatExchangerComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNodes(ent.Owner, ent.Comp.InletName, ent.Comp.OutletName,
                out PipeNode? inlet, out PipeNode? outlet))
            return;

        var inletAir = inlet.Air;
        var outletAir = outlet.Air;

        // Both nodes on the same pipeline, or a node that isn't hooked up to a pipeline at all.
        if (ReferenceEquals(inletAir, outletAir) || inletAir.Immutable || outletAir.Immutable)
            return;

        var inletTemperature = inletAir.Temperature;
        var outletTemperature = outletAir.Temperature;

        if (MathF.Abs(inletTemperature - outletTemperature) < ent.Comp.MinimumTemperatureDelta)
            return;

        var inletHeatCapacity = _atmosphere.GetHeatCapacity(inletAir, true);
        var outletHeatCapacity = _atmosphere.GetHeatCapacity(outletAir, true);
        var combinedHeatCapacity = inletHeatCapacity + outletHeatCapacity;

        if (combinedHeatCapacity <= Atmospherics.MinimumHeatCapacity)
            return;

        // Energy conserving equilibrium temperature, the same result /tg/station gets from
        // (combined energy) / (combined heat capacity).
        var equilibrium = (inletTemperature * inletHeatCapacity + outletTemperature * outletHeatCapacity) /
                          combinedHeatCapacity;

        inletAir.Temperature = MathF.Max(equilibrium, Atmospherics.TCMB);
        outletAir.Temperature = MathF.Max(equilibrium, Atmospherics.TCMB);
    }
}
