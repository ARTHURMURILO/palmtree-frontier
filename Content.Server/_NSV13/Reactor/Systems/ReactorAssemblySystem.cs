using System.Linq;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Construction.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server._NSV13.Reactor.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Maps;
using Content.Shared.NodeContainer;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Content.Shared._NSV13.Reactor;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Handles reactor flatpacks: validates the footprint before spawning, lays the site works around the
///     fresh reactor, and packs a cold, shut-down reactor back into its flatpack.
/// </summary>
public sealed class ReactorAssemblySystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ITileDefinitionManager _tileManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FlatpackComponent, FlatpackUnpackAttemptEvent>(OnFlatpackAttempt);
        SubscribeLocalEvent<FlatpackUnpackedEvent>(OnFlatpackUnpacked);

        SubscribeLocalEvent<ReactorAssemblyComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ReactorAssemblyComponent, ReactorDeconstructDoAfterEvent>(OnDeconstructDoAfter);
    }

    private void OnFlatpackAttempt(Entity<FlatpackComponent> ent, ref FlatpackUnpackAttemptEvent args)
    {
        if (!TryComp<ReactorFlatpackComponent>(ent, out var pack))
            return;

        if (!TryComp<MapGridComponent>(args.Grid, out var grid))
        {
            Cancel(ref args, "reactor-flatpack-no-grid");
            return;
        }

        var half = new Vector2i(pack.Footprint.X / 2, pack.Footprint.Y / 2);
        for (var x = -half.X; x <= half.X; x++)
        {
            for (var y = -half.Y; y <= half.Y; y++)
            {
                var pos = args.Position + new Vector2i(x, y);

                if (!_map.TryGetTileRef(args.Grid, grid, pos, out var tileRef) || tileRef.Tile.IsEmpty)
                {
                    Cancel(ref args, "reactor-flatpack-needs-floor");
                    return;
                }

                // The site has to be bare subfloor (plating or its scaffolding). A finished floor
                // - steel, carpet, wood - sits above the deck and would be covered by the reactor
                // without the crew deliberately pulling it up first.
                if (_tileManager[tileRef.Tile.TypeId] is ContentTileDefinition tileDef &&
                    (!tileDef.IsSubFloor || tileDef.ID == ContentTileDefinition.SpaceID))
                {
                    Cancel(ref args, "reactor-flatpack-needs-bare-floor");
                    return;
                }

                foreach (var anchored in _map.GetAnchoredEntities((args.Grid, grid), pos))
                {
                    // Pumps, filters, mixers and vents keep working under a buried reactor and
                    // would be impossible to reach. No pipe layer makes an atmos device okay.
                    if (HasComp<AtmosDeviceComponent>(anchored))
                    {
                        Cancel(ref args, "reactor-flatpack-atmos-device");
                        return;
                    }

                    // Primary-layer piping would be capped by the reactor's ports and its own
                    // stubs. Pipes on another layer stay separate and reachable, so they pass.
                    if (HasPrimaryLayerPipe(anchored))
                    {
                        Cancel(ref args, "reactor-flatpack-pipe");
                        return;
                    }

                    // Cables and off-layer pipes are expected under a reactor and are reused.
                    if (HasComp<CableComponent>(anchored) || HasPipe(anchored))
                        continue;

                    Cancel(ref args, "reactor-flatpack-blocked");
                    return;
                }
            }
        }
    }

    private void Cancel(ref FlatpackUnpackAttemptEvent args, LocId message)
    {
        args.Cancelled = true;
        args.Message = Loc.GetString(message);
    }

    private void OnFlatpackUnpacked(FlatpackUnpackedEvent args)
    {
        if (!TryComp<ReactorFlatpackComponent>(args.Flatpack, out var pack))
            return;

        if (TerminatingOrDeleted(args.Spawned))
            return;

        var reactor = args.Spawned;

        // MachineComponent fills two hidden containers on MapInit: the circuitboard that would
        // normally build this machine, and the stock parts it needs. A flatpack is the finished
        // reactor, not a kit, so the crew never supplied those. Delete them instead of leaving
        // them inside: otherwise every unpack/deconstruct cycle mints a free board and parts,
        // and the board can be used in a machine frame to build a second reactor.
        StripMachineInternals(reactor);

        var xform = Transform(reactor);
        if (!xform.Anchored)
            _transform.AnchorEntity(reactor);

        var assembly = EnsureComp<ReactorAssemblyComponent>(reactor);
        assembly.DeconstructDelay = pack.DeconstructDelay;
        assembly.ColdLimit = pack.ColdLimit;
        if (MetaData(args.Flatpack).EntityPrototype is { } flatpackProto)
            assembly.Flatpack = flatpackProto.ID;

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var tiles = FootprintTiles(center, pack.Footprint);

        if (pack.Floor != null)
        {
            var floor = _tileManager[pack.Floor];
            assembly.FloorTile = floor.TileId;

            foreach (var pos in tiles)
            {
                if (!_map.TryGetTileRef(gridUid, grid, pos, out var tileRef))
                    continue;

                assembly.Floors.Add((pos, tileRef.Tile));
                _map.SetTile(gridUid, grid, pos, new Tile(floor.TileId));
            }
        }

        if (pack.CableOnFootprint)
        {
            foreach (var pos in tiles)
            {
                if (HasCable(gridUid, grid, pos))
                    continue;

                assembly.Parts.Add(Spawn("CableHV", _map.GridTileToLocal(gridUid, grid, pos)));
            }
        }

        if (pack.PipesOnPorts && TryComp<NodeContainerComponent>(reactor, out var nodes))
        {
            foreach (var node in nodes.Nodes.Values)
            {
                if (node is not PipeNode pipe)
                    continue;

                var portDir = pipe.CurrentPipeDirection.ToDirection();
                var pos = center.Offset(portDir);
                if (HasConnectingPipe(gridUid, grid, pos, pipe, portDir))
                    continue;

                var pipeEnt = Spawn("GasPipeStraight", _map.GridTileToLocal(gridUid, grid, pos));

                // A straight pipe runs north-south at zero rotation. Only the moderator port is on
                // that axis, so copying the reactor's rotation would lay the two coolant stubs
                // across their ports, where their nodes never join the reactor's pipe net.
                _transform.SetLocalRotation(pipeEnt, portDir.ToAngle() - Direction.North.ToAngle());
                if (!Transform(pipeEnt).Anchored)
                    _transform.AnchorEntity(pipeEnt);
                assembly.Parts.Add(pipeEnt);
            }
        }
    }

    /// <summary>
    ///     Deletes the board and stock parts that <see cref="MachineComponent"/> spawns into every
    ///     machine on MapInit. A flatpack-spawned reactor is complete on arrival; those internals
    ///     would otherwise be free loot hidden inside it, and the board can rebuild the reactor.
    /// </summary>
    private void StripMachineInternals(EntityUid reactor)
    {
        if (!TryComp<ContainerManagerComponent>(reactor, out var containers))
            return;

        foreach (var name in new[] { MachineFrameComponent.BoardContainerName, MachineFrameComponent.PartContainerName })
        {
            if (containers.Containers.TryGetValue(name, out var container))
                _container.CleanContainer(container);
        }
    }

    private static List<Vector2i> FootprintTiles(Vector2i center, Vector2i footprint)
    {
        var tiles = new List<Vector2i>();
        var half = new Vector2i(footprint.X / 2, footprint.Y / 2);

        for (var x = -half.X; x <= half.X; x++)
        {
            for (var y = -half.Y; y <= half.Y; y++)
                tiles.Add(center + new Vector2i(x, y));
        }

        return tiles;
    }

    private bool HasCable(EntityUid gridUid, MapGridComponent grid, Vector2i pos)
    {
        foreach (var anchored in _map.GetAnchoredEntities((gridUid, grid), pos))
        {
            // Only an existing HV run can stand in for the mesh tile. An APC or MV cable through
            // the footprint must not skip the HV cable, or the reactor's take-off is left unwired.
            if (TryComp<CableComponent>(anchored, out var cable) && cable.CableType == CableType.HighVoltage)
                return true;
        }

        return false;
    }

    private bool HasPipe(EntityUid entity)
    {
        return TryComp<NodeContainerComponent>(entity, out var container)
               && container.Nodes.Values.Any(node => node is PipeNode);
    }

    /// <summary>
    ///     Whether the entity carries a pipe on the primary atmos layer, which is the layer the
    ///     reactor's own ports and stubs live on. Pipes on other layers do not join that line.
    /// </summary>
    private bool HasPrimaryLayerPipe(EntityUid entity)
    {
        if (!TryComp<NodeContainerComponent>(entity, out var container))
            return false;

        return container.Nodes.Values.Any(node =>
            node is PipeNode pipe && pipe.CurrentPipeLayer == AtmosPipeLayer.Primary);
    }

    /// <summary>
    ///     Whether the port tile already carries a pipe that actually faces this port. A pipe laid
    ///     across the port (or on another layer) does not join the reactor's net, so the flatpack
    ///     still lays its own stub.
    /// </summary>
    private bool HasConnectingPipe(EntityUid gridUid, MapGridComponent grid, Vector2i pos, PipeNode port, Direction portDir)
    {
        foreach (var anchored in _map.GetAnchoredEntities((gridUid, grid), pos))
        {
            if (!TryComp<NodeContainerComponent>(anchored, out var container))
                continue;

            foreach (var node in container.Nodes.Values)
            {
                if (node is not PipeNode other)
                    continue;

                if (other.NodeGroupID != port.NodeGroupID || other.CurrentPipeLayer != port.CurrentPipeLayer)
                    continue;

                if (other.CurrentPipeDirection.HasDirection(portDir.GetOpposite().ToPipeDirection()))
                    return true;
            }
        }

        return false;
    }

    private void OnGetVerbs(Entity<ReactorAssemblyComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var reason = GetBlockReason(ent);

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("reactor-deconstruct-verb"),
            Act = () => StartDeconstruct(ent, user),
            Disabled = reason != null,
            Message = reason,
            Impact = LogImpact.High,
        });
    }

    private string? GetBlockReason(Entity<ReactorAssemblyComponent> ent)
    {
        if (TryComp<RbmkReactorComponent>(ent, out var rbmk))
        {
            if (rbmk.Slagged)
                return Loc.GetString("reactor-deconstruct-melted");

            if (rbmk.IsRunning)
                return Loc.GetString("reactor-deconstruct-running");

            if (rbmk.Temperature > ent.Comp.ColdLimit)
                return Loc.GetString("reactor-deconstruct-hot");

            return null;
        }

        if (TryComp<StormdriveComponent>(ent, out var drive))
        {
            if (drive.ReactorEndTimes || drive.State == StormdriveState.Meltdown)
                return Loc.GetString("reactor-deconstruct-melted");

            if (drive.State is StormdriveState.Repair or StormdriveState.Reinforce or StormdriveState.Refit)
                return Loc.GetString("reactor-deconstruct-repair");

            if (drive.State == StormdriveState.Running)
                return Loc.GetString("reactor-deconstruct-running");

            if (drive.Heat > ent.Comp.ColdLimit)
                return Loc.GetString("reactor-deconstruct-hot");

            return null;
        }

        return Loc.GetString("reactor-deconstruct-invalid");
    }

    private void StartDeconstruct(Entity<ReactorAssemblyComponent> ent, EntityUid user)
    {
        var args = new DoAfterArgs(EntityManager,
            user,
            ent.Comp.DeconstructDelay,
            new ReactorDeconstructDoAfterEvent(),
            ent,
            target: ent)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(args);
    }

    private void OnDeconstructDoAfter(Entity<ReactorAssemblyComponent> ent, ref ReactorDeconstructDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // Two players can finish their do-afters on the same tick. The first already handed the
        // flatpack back and queued the reactor for deletion, so the second must not refund another.
        if (TerminatingOrDeleted(ent.Owner) || EntityManager.IsQueuedForDeletion(ent.Owner))
            return;

        var reason = GetBlockReason(ent);
        if (reason != null)
        {
            _popup.PopupEntity(reason, ent, args.User);
            return;
        }

        args.Handled = true;
        Deconstruct(ent, args.User);
    }

    private void Deconstruct(Entity<ReactorAssemblyComponent> ent, EntityUid user)
    {
        var uid = ent.Owner;
        var xform = Transform(uid);
        var coords = xform.Coordinates;
        var rotation = xform.LocalRotation;

        // The rods (and any fitted parts) come out before the machine goes, or they would go with it.
        // The machine's hidden board and stock-part containers are not contents: they are an
        // artifact of MachineComponent and must be deleted with the reactor, never ejected. New
        // flatpacks are stripped on unpack; this also covers a reactor from an older save.
        if (TryComp<ContainerManagerComponent>(uid, out var containers))
        {
            foreach (var (name, container) in containers.Containers)
            {
                if (name is MachineFrameComponent.BoardContainerName or MachineFrameComponent.PartContainerName)
                    continue;

                if (container.Count > 0)
                    _container.EmptyContainer(container, destination: coords);
            }
        }

        foreach (var part in ent.Comp.Parts)
        {
            if (!TerminatingOrDeleted(part))
                QueueDel(part);
        }

        if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            foreach (var (pos, original) in ent.Comp.Floors)
            {
                // Only restore deck the flatpack itself laid; leave crew changes alone.
                if (_map.TryGetTileRef(gridUid, grid, pos, out var tileRef) && tileRef.Tile.TypeId == ent.Comp.FloorTile)
                    _map.SetTile(gridUid, grid, pos, original);
            }
        }

        var flatpack = Spawn(ent.Comp.Flatpack, coords);
        _transform.SetLocalRotation(flatpack, rotation);

        _popup.PopupEntity(Loc.GetString("reactor-deconstruct-done"), uid, user);
        _adminLogger.Add(LogType.Construction,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} packed {ToPrettyString(uid):entity} back into {ToPrettyString(flatpack):entity}");

        QueueDel(uid);
    }
}

