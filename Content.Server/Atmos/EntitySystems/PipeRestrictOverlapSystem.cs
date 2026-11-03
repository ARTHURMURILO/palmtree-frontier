using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Popups;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.Components;
using Content.Shared.NodeContainer;
using Content.Shared.RCD;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

/// <summary>
/// This handles restricting pipe-based entities from overlapping outlets/inlets with other entities.
/// </summary>
public sealed class PipeRestrictOverlapSystem : EntitySystem
{
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    private readonly List<EntityUid> _anchoredEntities = new();
    private EntityQuery<NodeContainerComponent> _nodeContainerQuery;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<PipeRestrictOverlapComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<PipeRestrictOverlapComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<RCDAtmosOverlapAttemptEvent>(OnRcdAtmosOverlapAttempt); // Palmtree: RPD placement preview

        _nodeContainerQuery = GetEntityQuery<NodeContainerComponent>();
    }

    private void OnAnchorStateChanged(Entity<PipeRestrictOverlapComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            return;

        if (HasComp<AnchorableComponent>(ent) && CheckOverlap(ent))
        {
            _popup.PopupEntity(Loc.GetString("pipe-restrict-overlap-popup-blocked", ("pipe", ent.Owner)), ent);
            _xform.Unanchor(ent, Transform(ent));
        }
    }

    private void OnAnchorAttempt(Entity<PipeRestrictOverlapComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_nodeContainerQuery.TryComp(ent, out var node))
            return;

        var xform = Transform(ent);
        if (CheckOverlap((ent, node, xform)))
        {
            _popup.PopupEntity(Loc.GetString("pipe-restrict-overlap-popup-blocked", ("pipe", ent.Owner)), ent, args.User);
            args.Cancel();
        }
    }

    /// <summary>
    /// Palmtree: rejects an RCD/RPD placement before it starts when the prototype it would build
    /// has pipe nodes that overlap an anchored pipe node on the same atmos layer. This is the same
    /// rule <see cref="CheckOverlap(EntityUid)"/> applies after anchoring, but evaluated against
    /// the prototype's nodes so valid overlaps (a vent on a pipe, or another layer) still pass.
    /// </summary>
    private void OnRcdAtmosOverlapAttempt(ref RCDAtmosOverlapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!args.Prototype.TryGetComponent<NodeContainerComponent>(out var builtContainer, EntityManager.ComponentFactory))
            return;

        var builtNodes = builtContainer.Nodes.Values.OfType<PipeNode>().ToList();

        if (builtNodes.Count == 0)
            return;

        var grid = _xform.GetGrid(args.Coordinates);

        if (grid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComp))
            return;

        // Locked-layer entities (the manifold) keep a layer per node; everything else uses the
        // entity's current layer, which the RPD layer variant already carries.
        var builtLayers = args.Prototype.TryGetComponent<AtmosPipeLayersComponent>(out var layers, EntityManager.ComponentFactory)
            ? layers
            : null;

        var indices = _map.TileIndicesFor(gridUid, gridComp, args.Coordinates);
        _anchoredEntities.Clear();
        _map.GetAnchoredEntities((gridUid, gridComp), indices, _anchoredEntities);

        foreach (var otherEnt in _anchoredEntities)
        {
            if (!_nodeContainerQuery.TryComp(otherEnt, out var otherContainer))
                continue;

            var otherXform = Transform(otherEnt);

            foreach (var node in otherContainer.Nodes.Values)
            {
                if (node is not PipeNode otherPipe)
                    continue;

                var otherDirection = otherPipe.OriginalPipeDirection.RotatePipeDirection(otherXform.LocalRotation);

                foreach (var builtPipe in builtNodes)
                {
                    var builtLayer = builtLayers is { PipeLayersLocked: false }
                        ? builtLayers.CurrentPipeLayer
                        : builtPipe.CurrentPipeLayer;

                    if (builtLayer != otherPipe.CurrentPipeLayer)
                        continue;

                    var builtDirection = builtPipe.OriginalPipeDirection.RotatePipeDirection(args.Rotation);

                    if ((builtDirection & otherDirection) == 0)
                        continue;

                    args.Cancelled = true;
                    return;
                }
            }
        }
    }

    [PublicAPI]
    public bool CheckOverlap(EntityUid uid)
    {
        if (!_nodeContainerQuery.TryComp(uid, out var node))
            return false;

        return CheckOverlap((uid, node, Transform(uid)));
    }

    public bool CheckOverlap(Entity<NodeContainerComponent, TransformComponent> ent)
    {
        if (ent.Comp2.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var indices = _map.TileIndicesFor(grid, gridComp, ent.Comp2.Coordinates);
        _anchoredEntities.Clear();
        _map.GetAnchoredEntities((grid, gridComp), indices, _anchoredEntities);

        foreach (var otherEnt in _anchoredEntities)
        {
            // this should never actually happen but just for safety
            if (otherEnt == ent.Owner)
                continue;

            if (!_nodeContainerQuery.TryComp(otherEnt, out var otherComp))
                continue;

            if (PipeNodesOverlap(ent, (otherEnt, otherComp, Transform(otherEnt))))
                return true;
        }

        return false;
    }

    public bool PipeNodesOverlap(Entity<NodeContainerComponent, TransformComponent> ent, Entity<NodeContainerComponent, TransformComponent> other)
    {
        var entDirsAndLayers = GetAllDirectionsAndLayers(ent).ToList();
        var otherDirsAndLayers = GetAllDirectionsAndLayers(other).ToList();

        foreach (var (dir, layer) in entDirsAndLayers)
        {
            foreach (var (otherDir, otherLayer) in otherDirsAndLayers)
            {
                if ((dir & otherDir) != 0 && layer == otherLayer)
                    return true;
            }
        }

        return false;

        IEnumerable<(PipeDirection, AtmosPipeLayer)> GetAllDirectionsAndLayers(Entity<NodeContainerComponent, TransformComponent> pipe)
        {
            foreach (var node in pipe.Comp1.Nodes.Values)
            {
                // we need to rotate the pipe manually like this because the rotation doesn't update for pipes that are unanchored.
                if (node is PipeNode pipeNode)
                    yield return (pipeNode.OriginalPipeDirection.RotatePipeDirection(pipe.Comp2.LocalRotation), pipeNode.CurrentPipeLayer);
            }
        }
    }
}
