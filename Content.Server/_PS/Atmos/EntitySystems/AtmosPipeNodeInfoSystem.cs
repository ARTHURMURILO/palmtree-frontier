using Content.Server.NodeContainer.Nodes;
using Content.Shared._PS.Atmos.Components;
using Content.Shared._PS.Atmos.Events;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.NodeContainer;
using JetBrains.Annotations;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: maintains the client-visible pipe node summary used by the RPD placement ghost, and
/// answers the client's request for a prototype's pipe nodes.
/// </summary>
/// <remarks>
/// The RCD overlap check runs on the client as well as the server, but the client cannot read
/// <see cref="NodeContainerComponent.Nodes"/> (it is <c>serverOnly</c>), and it cannot read a
/// prototype's nodes for the same reason. Anchored entities get a networked
/// <see cref="AtmosPipeNodeDirectionsComponent"/>; the prototype the placement would build is
/// resolved by the client and sent here once, then cached.
/// </remarks>
[UsedImplicitly]
public sealed partial class AtmosPipeNodeInfoSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private readonly Dictionary<string, List<AtmosPipeNodeInfo>> _prototypeNodes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NodeContainerStartedEvent>(OnNodeContainerStarted);
        SubscribeLocalEvent<NodeContainerComponent, MapInitEvent>(OnNodeContainerMapInit);
        SubscribeLocalEvent<AtmosPipeLayersComponent, AtmosPipeLayerChangedEvent>(OnPipeLayerChanged);
        SubscribeNetworkEvent<AtmosPipeNodeInfoRequestEvent>(OnNodeInfoRequest);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnNodeContainerStarted(NodeContainerStartedEvent args)
    {
        Refresh(args.Uid);
    }

    private void OnNodeContainerMapInit(EntityUid uid, NodeContainerComponent component, MapInitEvent args)
    {
        // Entities spawned onto an uninitialized map (mapping, PrototypeSaveTest) get their summary
        // when the map is initialized, so spawning one never adds a component to map-save data.
        Refresh(uid, component);
    }

    private void OnPipeLayerChanged(EntityUid uid, AtmosPipeLayersComponent component, ref AtmosPipeLayerChangedEvent args)
    {
        Refresh(uid);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _prototypeNodes.Clear();
    }

    private void OnNodeInfoRequest(AtmosPipeNodeInfoRequestEvent ev, EntitySessionEventArgs session)
    {
        // Keep the answer cached per prototype: the data is static for the round, and a client may
        // ask for the same ghost prototype on every placement.
        if (!_prototypeNodes.TryGetValue(ev.PrototypeId, out var nodes))
        {
            if (!_prototypeManager.TryIndex<EntityPrototype>(ev.PrototypeId, out var prototype))
            {
                // Never cache ids that do not resolve: PrototypeId comes from the client, so caching
                // them would let a malicious client grow this dictionary without bound. Answer with
                // an empty list so the client does not keep the request pending.
                RaiseNetworkEvent(
                    new AtmosPipeNodeInfoResponseEvent(ev.PrototypeId, new List<AtmosPipeNodeInfo>()),
                    session.SenderSession);
                return;
            }

            nodes = new List<AtmosPipeNodeInfo>();

            if (prototype.TryGetComponent<NodeContainerComponent>(out var container, EntityManager.ComponentFactory))
            {
                foreach (var node in container.Nodes.Values)
                {
                    if (node is PipeNode pipe)
                        nodes.Add(new AtmosPipeNodeInfo(pipe.OriginalPipeDirection, pipe.CurrentPipeLayer));
                }
            }

            _prototypeNodes[ev.PrototypeId] = nodes;
        }

        RaiseNetworkEvent(new AtmosPipeNodeInfoResponseEvent(ev.PrototypeId, nodes), session.SenderSession);
    }

    private void Refresh(EntityUid uid, NodeContainerComponent? container = null)
    {
        if (!Resolve(uid, ref container, false))
            return;

        // Only summarize entities on an initialized map. This keeps spawn-time code from adding a
        // component to an uninitialized map (which PrototypeSaveTest forbids because it would end
        // up in map-save data), and MapInitEvent fills the summary once the map is initialized.
        // Live ship saves create their map initialized, so they still get the summary on startup.
        if (!_map.IsInitialized(Transform(uid).MapID))
            return;

        AtmosPipeLayersComponent? layers = null;
        TryComp(uid, out layers);

        var primary = PipeDirection.None;
        var secondary = PipeDirection.None;
        var tertiary = PipeDirection.None;
        var hasPipeNode = false;

        foreach (var node in container.Nodes.Values)
        {
            if (node is not PipeNode pipe)
                continue;

            hasPipeNode = true;

            // Locked-layer entities (the manifold) keep a layer per node; everything else uses the
            // entity's current layer, mirroring PipeRestrictOverlapSystem.
            var layer = layers is { PipeLayersLocked: false }
                ? layers.CurrentPipeLayer
                : pipe.CurrentPipeLayer;

            switch (layer)
            {
                case AtmosPipeLayer.Primary:
                    primary |= pipe.OriginalPipeDirection;
                    break;
                case AtmosPipeLayer.Secondary:
                    secondary |= pipe.OriginalPipeDirection;
                    break;
                case AtmosPipeLayer.Tertiary:
                    tertiary |= pipe.OriginalPipeDirection;
                    break;
            }
        }

        if (!hasPipeNode)
        {
            RemCompDeferred<AtmosPipeNodeDirectionsComponent>(uid);
            return;
        }

        var component = EnsureComp<AtmosPipeNodeDirectionsComponent>(uid);

        if (component.Primary == primary &&
            component.Secondary == secondary &&
            component.Tertiary == tertiary)
        {
            return;
        }

        component.Primary = primary;
        component.Secondary = secondary;
        component.Tertiary = tertiary;
        Dirty(uid, component);
    }
}
