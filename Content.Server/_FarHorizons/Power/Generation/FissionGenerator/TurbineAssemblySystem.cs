using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared.Administration.Logs;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
///     Handles the gas turbine flatpacks: lays the reinforced plating and HV cabling the flatpack
///     carries, preserves the turbine's condition across a pack/unpack cycle, and lets a stopped
///     turbine be packed back into its flatpack.
/// </summary>
public sealed class TurbineAssemblySystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedFlatpackPlacementSystem _placement = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ITileDefinitionManager _tileManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlatpackUnpackedEvent>(OnFlatpackUnpacked);
        SubscribeLocalEvent<TurbineAssemblyComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<TurbineComponent, TurbinePackDoAfterEvent>(OnPackDoAfter);
    }

    private void OnFlatpackUnpacked(FlatpackUnpackedEvent args)
    {
        if (TerminatingOrDeleted(args.Spawned))
            return;

        if (!TryComp<TurbineAssemblyComponent>(args.Spawned, out var assembly))
            return;

        assembly.Deployed = true;
        RestorePackedState(args);

        // Only flatpacks that declare a footprint know which tiles the machine covers.
        if (!TryComp<FlatpackFootprintComponent>(args.Flatpack, out var footprint))
            return;

        var xform = Transform(args.Spawned);
        if (!xform.Anchored)
            _transform.AnchorEntity(args.Spawned);

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var tiles = _placement.GetFootprintTiles(center, footprint, xform.LocalRotation.GetCardinalDir());

        if (assembly.Floor != null)
        {
            var floor = _tileManager[assembly.Floor];
            assembly.FloorTile = floor.TileId;

            foreach (var pos in tiles)
            {
                if (!_map.TryGetTileRef(gridUid, grid, pos, out var tileRef))
                    continue;

                assembly.Floors.Add((pos, tileRef.Tile));
                _map.SetTile(gridUid, grid, pos, new Tile(floor.TileId));
            }
        }

        if (assembly.CableOnFootprint)
        {
            foreach (var pos in tiles)
            {
                if (HasHvCable(gridUid, grid, pos))
                    continue;

                assembly.Parts.Add(Spawn("CableHV", _map.GridTileToLocal(gridUid, grid, pos)));
            }
        }
    }

    /// <summary>
    ///     Restores the condition the turbine had when it was packed up. Without this, packing and
    ///     unpacking a damaged turbine would repair it for free.
    /// </summary>
    private void RestorePackedState(FlatpackUnpackedEvent args)
    {
        if (!TryComp<TurbineFlatpackStateComponent>(args.Flatpack, out var state) ||
            !TryComp<TurbineComponent>(args.Spawned, out var turbine))
        {
            return;
        }

        turbine.BladeHealth = Math.Clamp(state.BladeHealth, 0, turbine.BladeHealthMax);
        turbine.Ruined = state.Ruined;
        turbine.IsSparking = turbine.BladeHealth <= 0.75f * turbine.BladeHealthMax;
        turbine.IsSmoking = turbine.BladeHealth <= 0.5f * turbine.BladeHealthMax;
        turbine.StatorLoad = Math.Clamp(state.StatorLoad, 1000f, turbine.StatorLoadMax);
        turbine.FlowRate = Math.Clamp(state.FlowRate, 0f, turbine.FlowRateMax);
    }

    private bool HasHvCable(EntityUid gridUid, MapGridComponent grid, Vector2i pos)
    {
        foreach (var anchored in _map.GetAnchoredEntities((gridUid, grid), pos))
        {
            if (TryComp<CableComponent>(anchored, out var cable) && cable.CableType == CableType.HighVoltage)
                return true;
        }

        return false;
    }

    private void OnGetVerbs(Entity<TurbineAssemblyComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        // Map-spawned turbines are not packable; only the ones a flatpack deployed.
        if (!ent.Comp.Deployed || !args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var reason = GetPackBlockReason(ent.Owner);

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("turbine-pack-verb"),
            Act = () => StartPack(ent, user),
            Disabled = reason != null,
            Message = reason,
            Impact = LogImpact.Medium,
        });
    }

    private string? GetPackBlockReason(EntityUid uid)
    {
        if (!TryComp<TurbineComponent>(uid, out var turbine))
            return Loc.GetString("turbine-pack-invalid");

        return turbine.RPM > 1f ? Loc.GetString("turbine-pack-running") : null;
    }

    private void StartPack(Entity<TurbineAssemblyComponent> ent, EntityUid user)
    {
        var args = new DoAfterArgs(EntityManager,
            user,
            ent.Comp.PackDelay,
            new TurbinePackDoAfterEvent(),
            ent,
            target: ent)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(args);
    }

    private void OnPackDoAfter(Entity<TurbineComponent> ent, ref TurbinePackDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // Two players can finish their do-afters on the same tick. The first has already handed the
        // flatpack back and queued the turbine for deletion, so the second must not mint another.
        if (TerminatingOrDeleted(ent.Owner) || EntityManager.IsQueuedForDeletion(ent.Owner))
            return;

        var reason = GetPackBlockReason(ent.Owner);
        if (reason != null)
        {
            _popup.PopupEntity(reason, ent.Owner, args.User);
            return;
        }

        if (!TryComp<TurbineAssemblyComponent>(ent.Owner, out var assembly) || !assembly.Deployed)
            return;

        args.Handled = true;
        Pack(ent, assembly, args.User);
    }

    private void Pack(Entity<TurbineComponent> ent, TurbineAssemblyComponent assembly, EntityUid user)
    {
        var uid = ent.Owner;
        var xform = Transform(uid);
        var coords = xform.Coordinates;
        var rotation = xform.LocalRotation;

        PackPipes(ent.Comp);

        foreach (var part in assembly.Parts)
        {
            if (!TerminatingOrDeleted(part))
                QueueDel(part);
        }
        assembly.Parts.Clear();

        if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            foreach (var (pos, original) in assembly.Floors)
            {
                // Only restore the deck the flatpack itself laid; leave crew changes alone.
                if (_map.TryGetTileRef(gridUid, grid, pos, out var tileRef) && tileRef.Tile.TypeId == assembly.FloorTile)
                    _map.SetTile(gridUid, grid, pos, original);
            }
        }
        assembly.Floors.Clear();

        var flatpack = Spawn(assembly.Flatpack, coords);
        _transform.SetLocalRotation(flatpack, rotation);

        var state = EnsureComp<TurbineFlatpackStateComponent>(flatpack);
        state.BladeHealth = ent.Comp.BladeHealth;
        state.Ruined = ent.Comp.Ruined;
        state.StatorLoad = ent.Comp.StatorLoad;
        state.FlowRate = ent.Comp.FlowRate;

        _popup.PopupEntity(Loc.GetString("turbine-pack-done"), uid, user);
        _adminLogger.Add(LogType.Construction, LogImpact.Medium,
            $"{ToPrettyString(user):player} packed {ToPrettyString(uid):entity} back into {ToPrettyString(flatpack):entity}");

        QueueDel(uid);
    }

    /// <summary>
    ///     The pipe stubs are part of the turbine and vanish with it. Spill whatever is inside them
    ///     onto the deck first, so packing up does not silently delete the gas.
    /// </summary>
    private void PackPipes(TurbineComponent turbine)
    {
        VentPipe(turbine.InletEnt, turbine.PipeName);
        VentPipe(turbine.OutletEnt, turbine.PipeName);

        QueueDel(turbine.InletEnt);
        QueueDel(turbine.OutletEnt);
        turbine.InletEnt = null;
        turbine.OutletEnt = null;
    }

    private void VentPipe(EntityUid? pipeUid, string pipeName)
    {
        if (pipeUid is not { } pipe || TerminatingOrDeleted(pipe))
            return;

        if (!_nodeContainer.TryGetNode(pipe, pipeName, out PipeNode? node))
            return;

        var tile = _atmosphere.GetTileMixture(pipe, excite: true);
        if (tile != null)
            _atmosphere.Merge(tile, node.Air);
    }
}
