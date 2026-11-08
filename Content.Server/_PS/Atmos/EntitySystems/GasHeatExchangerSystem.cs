using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._PS.Atmos.Components;
using Content.Shared.Atmos;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: runs a pair of heat exchangers that have been placed facing each other. Both
/// pipelines are brought to the heat-capacity weighted equilibrium temperature, without any gas
/// moving between them.
/// </summary>
[UsedImplicitly]
public sealed class GasHeatExchangerSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly MapSystem _map = default!;

    private readonly List<EntityUid> _anchored = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasHeatExchangerComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnAtmosUpdate(Entity<GasHeatExchangerComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNode(ent.Owner, ent.Comp.PipeNodeName, out PipeNode? node))
            return;

        if (!TryGetPartner(ent, node, out _, out var partnerNode))
            return;

        var air = node.Air;
        var partnerAir = partnerNode.Air;

        // A node that is not hooked up to a pipeline at all resolves to the immutable space
        // mixture, which must not be written to.
        if (air.Immutable || partnerAir.Immutable)
            return;

        if (ReferenceEquals(air, partnerAir))
            return;

        if (MathF.Abs(air.Temperature - partnerAir.Temperature) < ent.Comp.MinimumTemperatureDelta)
            return;

        var heatCapacity = _atmosphere.GetHeatCapacity(air, true);
        var partnerHeatCapacity = _atmosphere.GetHeatCapacity(partnerAir, true);
        var combinedHeatCapacity = heatCapacity + partnerHeatCapacity;

        if (combinedHeatCapacity <= Atmospherics.MinimumHeatCapacity)
            return;

        // Energy conserving equilibrium temperature, the same result /tg/station gets from
        // (combined energy) / (combined heat capacity).
        var equilibrium = (air.Temperature * heatCapacity + partnerAir.Temperature * partnerHeatCapacity) /
                          combinedHeatCapacity;

        air.Temperature = MathF.Max(equilibrium, Atmospherics.TCMB);
        partnerAir.Temperature = MathF.Max(equilibrium, Atmospherics.TCMB);
    }

    /// <summary>
    /// Finds the heat exchanger this one is facing. Both devices stand next to each other with
    /// their flat faces touching, so the partner is the anchored heat exchanger in the tile the
    /// device faces, with its own pipe on the far side.
    /// </summary>
    private bool TryGetPartner(Entity<GasHeatExchangerComponent> ent,
        PipeNode node,
        out Entity<GasHeatExchangerComponent> partner,
        out PipeNode partnerNode)
    {
        partner = default;
        partnerNode = default!;

        if (node.CurrentPipeDirection is not (PipeDirection.North or PipeDirection.South or
            PipeDirection.East or PipeDirection.West))
        {
            return false;
        }

        var xform = Transform(ent);

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        // The device faces away from its pipe, and the partner has to face back at it.
        var faceDirection = node.CurrentPipeDirection.GetOpposite().ToDirection();
        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates).Offset(faceDirection);

        _anchored.Clear();
        _map.GetAnchoredEntities((gridUid, grid), indices, _anchored);

        foreach (var other in _anchored)
        {
            if (other == ent.Owner ||
                !TryComp<GasHeatExchangerComponent>(other, out var otherComp) ||
                !_nodeContainer.TryGetNode(other, otherComp.PipeNodeName, out PipeNode? otherNode))
            {
                continue;
            }

            // The partner's pipe has to point away from this device. Two devices placed back to
            // back have their pipes pointing at each other instead: those are just two devices on
            // one pipeline, not a heat exchanger pair.
            if (otherNode.CurrentPipeDirection != faceDirection.ToPipeDirection())
                continue;

            if (otherNode.CurrentPipeLayer != node.CurrentPipeLayer)
                continue;

            partner = (other, otherComp);
            partnerNode = otherNode;
            return true;
        }

        return false;
    }
}
