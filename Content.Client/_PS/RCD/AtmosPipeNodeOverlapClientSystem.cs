using Content.Shared._PS.Atmos.Components;
using Content.Shared._PS.Atmos.Events;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.RCD;
using JetBrains.Annotations;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._PS.RCD;

/// <summary>
/// Palmtree: answers <see cref="RCDAtmosOverlapAttemptEvent"/> on the client, so the RPD placement
/// ghost turns red when the server would reject the placement for overlapping pipe nodes.
/// </summary>
/// <remarks>
/// The client cannot read <see cref="Content.Shared.NodeContainer.NodeContainerComponent.Nodes"/>
/// directly. Anchored entities carry <see cref="AtmosPipeNodeDirectionsComponent"/> (networked), and
/// the prototype being placed is requested from the server once and cached.
/// </remarks>
[UsedImplicitly]
public sealed partial class AtmosPipeNodeOverlapClientSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly Dictionary<string, List<AtmosPipeNodeInfo>> _prototypeNodes = new();
    private readonly HashSet<string> _pendingRequests = new();
    private readonly List<EntityUid> _anchored = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AtmosPipeNodeInfoResponseEvent>(OnNodeInfoResponse);
        SubscribeLocalEvent<RCDAtmosOverlapAttemptEvent>(OnRcdAtmosOverlapAttempt);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnNodeInfoResponse(AtmosPipeNodeInfoResponseEvent ev, EntitySessionEventArgs session)
    {
        _pendingRequests.Remove(ev.PrototypeId);
        _prototypeNodes[ev.PrototypeId] = ev.Nodes;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _prototypeNodes.Clear();
        _pendingRequests.Clear();
    }

    private void OnRcdAtmosOverlapAttempt(ref RCDAtmosOverlapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        // The prototype's node data is server-only; ask for it once and let the next frame decide.
        if (!_prototypeNodes.TryGetValue(args.Prototype.ID, out var builtNodes))
        {
            RequestNodeInfo(args.Prototype.ID);
            return;
        }

        if (builtNodes.Count == 0)
            return;

        var grid = _xform.GetGrid(args.Coordinates);

        if (grid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComp))
            return;

        // Mirrors the server handler: locked-layer prototypes (the manifold) keep a layer per node;
        // everything else uses the layer of the resolved prototype.
        var builtLayers = args.Prototype.TryGetComponent<AtmosPipeLayersComponent>(out var layers, _componentFactory)
            ? layers
            : null;

        var indices = _map.TileIndicesFor(gridUid, gridComp, args.Coordinates);
        _anchored.Clear();
        _map.GetAnchoredEntities((gridUid, gridComp), indices, _anchored);

        foreach (var other in _anchored)
        {
            if (!TryComp<AtmosPipeNodeDirectionsComponent>(other, out var otherDirections))
                continue;

            var otherRotation = Transform(other).LocalRotation;

            foreach (var built in builtNodes)
            {
                var builtLayer = builtLayers is { PipeLayersLocked: false }
                    ? builtLayers.CurrentPipeLayer
                    : built.Layer;

                var builtDirection = built.Direction.RotatePipeDirection(args.Rotation);

                if (Overlaps(otherDirections, builtLayer, builtDirection, otherRotation))
                {
                    args.Cancelled = true;
                    return;
                }
            }
        }
    }

    private static bool Overlaps(
        AtmosPipeNodeDirectionsComponent directions,
        AtmosPipeLayer layer,
        PipeDirection builtDirection,
        Angle otherRotation)
    {
        var otherDirections = directions.GetDirections(layer);

        if (otherDirections == PipeDirection.None)
            return false;

        return (builtDirection & otherDirections.RotatePipeDirection(otherRotation)) != 0;
    }

    private void RequestNodeInfo(string prototypeId)
    {
        if (!_pendingRequests.Add(prototypeId))
            return;

        RaiseNetworkEvent(new AtmosPipeNodeInfoRequestEvent(prototypeId));
    }
}
