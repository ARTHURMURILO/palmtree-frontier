// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus <90893484+LaCumbiaDelCoronavirus@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 marc-pelletier <113944176+marc-pelletier@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Server._Funkystation.Atmos.Components;
using Content.Server.Atmos.Piping.Components;
using Content.Shared._Funkystation.Atmos.Components;
using Content.Shared.Atmos;
using Content.Server.Atmos.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using System.Linq;
using Content.Shared._Funkystation.Atmos.Visuals;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server._Funkystation.Atmos.HFR.Systems;

namespace Content.Server._Funkystation.Atmos.Systems;

public sealed class HFRCoreSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private readonly HFRConsoleSystem _hfrConsoleSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly HypertorusFusionReactorSystem _hfrSystem = default!;
    [Dependency] private readonly AtmosphereSystem _atmosSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;

    public static readonly Vector2i[] CardinalOffsets = [Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right];
    public static readonly Vector2i[] DiagonalOffsets = [new(1, 1), new(-1, 1), new(-1, -1), new(1, -1)];

    private readonly (Type ComponentType, Action<HFRCoreComponent, EntityUid?> SetField)[] _singleComponents = [
        (typeof(HFRConsoleComponent), (core, uid) => core.ConsoleUid = uid),
        (typeof(HFRFuelInputComponent), (core, uid) => core.FuelInputUid = uid),
        (typeof(HFRModeratorInputComponent), (core, uid) => core.ModeratorInputUid = uid),
        (typeof(HFRWasteOutputComponent), (core, uid) => core.WasteOutputUid = uid)
    ];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HFRCoreComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<HFRCoreComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<HFRCoreComponent, AtmosDeviceUpdateEvent>(OnDeviceAtmosUpdate);
        SubscribeLocalEvent<HFRCoreComponent, ComponentShutdown>(OnCoreShutdown);
    }

    /// <summary>
    /// Palmtree: clear the part links in both directions when the core is removed/deleted. The old
    /// code had no cleanup at all, so a deleted core left every part pointing at a UID that could be
    /// recycled by a later spawn.
    /// </summary>
    private void OnCoreShutdown(EntityUid uid, HFRCoreComponent core, ref ComponentShutdown args)
    {
        // Palmtree: a deleted core must not leave its parts active - HFRIsActive would block welding
        // the survivors back together. Deactivate first (the core itself is already terminating, so
        // ToggleActiveState's own Anchorable restore guard skips it).
        if (core.IsActive)
            _hfrSystem.ToggleActiveState(uid, core, false);

        if (core.ConsoleUid is { } consoleUid && TryComp<HFRConsoleComponent>(consoleUid, out var consoleComp))
        {
            consoleComp.CoreUid = null;
            _hfrConsoleSystem.SetPowerState(consoleUid, consoleComp);
        }

        if (core.FuelInputUid is { } fuelUid && TryComp<HFRFuelInputComponent>(fuelUid, out var fuelComp))
            fuelComp.CoreUid = null;

        if (core.ModeratorInputUid is { } moderatorUid && TryComp<HFRModeratorInputComponent>(moderatorUid, out var modComp))
            modComp.CoreUid = null;

        if (core.WasteOutputUid is { } wasteUid && TryComp<HFRWasteOutputComponent>(wasteUid, out var wasteComp))
            wasteComp.CoreUid = null;

        foreach (var cornerUid in core.CornerUids)
        {
            if (TryComp<HFRCornerComponent>(cornerUid, out var cornerComp))
                cornerComp.CoreUid = null;
        }

        core.ConsoleUid = null;
        core.FuelInputUid = null;
        core.ModeratorInputUid = null;
        core.WasteOutputUid = null;
        core.CornerUids.Clear();
    }

    private void OnStartup(EntityUid uid, HFRCoreComponent core, ComponentStartup args)
    {
        TryLinkSurroundingComponents(uid, core);
    }

    private void OnAnchorChanged(EntityUid uid, HFRCoreComponent core, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
        {
            core.IsActive = false;
            if (TryComp<AppearanceComponent>(uid, out var coreAppearance))
            {
                _appearanceSystem.SetData(uid, HFRVisuals.IsActive, false, coreAppearance);
            }

            foreach (var (compType, setField) in _singleComponents)
            {
                EntityUid? compUid = compType == typeof(HFRConsoleComponent) ? core.ConsoleUid :
                                    compType == typeof(HFRFuelInputComponent) ? core.FuelInputUid :
                                    compType == typeof(HFRModeratorInputComponent) ? core.ModeratorInputUid :
                                    core.WasteOutputUid;

                if (compUid != null && EntityManager.HasComponent(compUid.Value, compType))
                {
                    if (compType == typeof(HFRConsoleComponent))
                    {
                        if (EntityManager.TryGetComponent<HFRConsoleComponent>(compUid.Value, out var consoleComp))
                        {
                            consoleComp.CoreUid = null;
                            _hfrConsoleSystem.SetPowerState(compUid.Value, consoleComp);
                        }
                    }
                    else if (compType == typeof(HFRFuelInputComponent))
                    {
                        if (EntityManager.TryGetComponent<HFRFuelInputComponent>(compUid.Value, out var fuelComp))
                        {
                            fuelComp.CoreUid = null;
                        }
                    }
                    else if (compType == typeof(HFRModeratorInputComponent))
                    {
                        if (EntityManager.TryGetComponent<HFRModeratorInputComponent>(compUid.Value, out var modComp))
                        {
                            modComp.CoreUid = null;
                        }
                    }
                    else if (compType == typeof(HFRWasteOutputComponent))
                    {
                        if (EntityManager.TryGetComponent<HFRWasteOutputComponent>(compUid.Value, out var wasteComp))
                        {
                            wasteComp.CoreUid = null;
                        }
                    }
                }

                // Clear stale references even when the part has already been deleted. Leaving a
                // dead UID here makes the core think its console is still present and powered.
                setField(core, null);
            }

            foreach (var cornerUid in core.CornerUids.ToList())
            {
                if (EntityManager.TryGetComponent<HFRCornerComponent>(cornerUid, out var cornerComp))
                {
                    cornerComp.CoreUid = null;
                }
                core.CornerUids.Remove(cornerUid);
            }

            _hfrSystem.ToggleActiveState(uid, core, false);
        }
        else
        {
            TryLinkSurroundingComponents(uid, core);
            _hfrSystem.UpdateConsolePowerState(core);
        }
    }

    private void OnDeviceAtmosUpdate(EntityUid uid, HFRCoreComponent core, ref AtmosDeviceUpdateEvent args)
    {
        if (core.InternalFusion != null)
            _atmosSystem.React(core.InternalFusion, null);
        if (core.ModeratorInternal != null)
            _atmosSystem.React(core.ModeratorInternal, null);

        // Palmtree: use the atmos device event's dt. Deriving it from CurTime means a paused or
        // skipped atmos tick produces a huge delta that is applied as one step of heat.
        _hfrSystem.Process(uid, core, args.dt);
        UpdateReactorUI(uid, core);
    }

    private void UpdateReactorUI(EntityUid uid, HFRCoreComponent? core)
    {
        if (!Resolve(uid, ref core, false))
            return;

        if (core.ConsoleUid == null || !EntityManager.EntityExists(core.ConsoleUid.Value))
            return;

        if (!_userInterfaceSystem.IsUiOpen(core.ConsoleUid.Value, HFRConsoleUiKey.Key))
            return;

        var internalFusionMoles = new Dictionary<Gas, float>();
        if (core.InternalFusion != null)
        {
            foreach (Gas gas in Enum.GetValues(typeof(Gas)))
            {
                float moles = core.InternalFusion.GetMoles(gas);
                if (moles > 0)
                    internalFusionMoles[gas] = moles;
            }
        }

        var moderatorInternalMoles = new Dictionary<Gas, float>();
        if (core.ModeratorInternal != null)
        {
            foreach (Gas gas in Enum.GetValues(typeof(Gas)))
            {
                float moles = core.ModeratorInternal.GetMoles(gas);
                if (moles > 0)
                    moderatorInternalMoles[gas] = moles;
            }
        }

        // Calculate coolant moles and temperature from HFR core pipe
        float coolantMoles = 0f;
        float coolantTemperature = 0f;
        float coolantTemperatureArchived = 0f;
        if (_nodeContainer.TryGetNode(uid, "pipe", out PipeNode? corePipe) && corePipe.Air != null)
        {
            coolantMoles = corePipe.Air.TotalMoles;
            coolantTemperature = corePipe.Air.Temperature;
            coolantTemperatureArchived = coolantTemperature;
        }

        // Calculate output moles and temperature from HFRWasteOutput pipe
        float outputMoles = 0f;
        float outputTemperature = 0f;
        float outputTemperatureArchived = 0f;
        if (core.WasteOutputUid != null &&
            _nodeContainer.TryGetNode(core.WasteOutputUid.Value, "pipe", out PipeNode? wastePipe) &&
            wastePipe.Air != null)
        {
            outputMoles = wastePipe.Air.TotalMoles;
            outputTemperature = wastePipe.Air.Temperature;
            outputTemperatureArchived = outputTemperature;
        }

        _userInterfaceSystem.ServerSendUiMessage(core.ConsoleUid.Value, HFRConsoleUiKey.Key,
            new HFRConsoleUpdateReactorMessage(
                internalFusionMoles,
                moderatorInternalMoles,
                core.CriticalThresholdProximity,
                core.MeltingPoint,
                core.IronContent,
                core.AreaPower,
                core.PowerLevel,
                core.Energy,
                core.Efficiency,
                core.Instability,
                core.SelectedRecipeId,
                core.FusionTemperature,
                core.FusionTemperatureArchived,
                core.ModeratorTemperature,
                core.ModeratorTemperatureArchived,
                coolantTemperature,
                coolantTemperatureArchived,
                outputTemperature,
                outputTemperatureArchived,
                coolantMoles,
                outputMoles));
    }

    public bool TryLinkComponent<TComp>(EntityUid coreUid, HFRCoreComponent core, EntityUid componentUid) where TComp : Component
    {
        TryLinkSurroundingComponents(coreUid, core);
        EntityUid? linkedUid = typeof(TComp) == typeof(HFRConsoleComponent) ? core.ConsoleUid :
            typeof(TComp) == typeof(HFRFuelInputComponent) ? core.FuelInputUid :
            typeof(TComp) == typeof(HFRModeratorInputComponent) ? core.ModeratorInputUid :
            typeof(TComp) == typeof(HFRWasteOutputComponent) ? core.WasteOutputUid : null;
        return linkedUid == componentUid;
    }

    public bool TryLinkCorner(EntityUid coreUid, HFRCoreComponent core, EntityUid cornerUid)
    {
        TryLinkSurroundingComponents(coreUid, core);
        return core.CornerUids.Contains(cornerUid);
    }

    private void TryLinkSurroundingComponents(EntityUid coreUid, HFRCoreComponent core)
    {
        if (!TryComp<TransformComponent>(coreUid, out var coreXform) || !coreXform.Anchored)
            return;

        var gridUid = coreXform.GridUid;
        if (gridUid == null || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var coreCoords = _transformSystem.GetMapCoordinates(coreUid);
        var coreTile = _mapSystem.CoordinatesToTile(gridUid.Value, grid, coreCoords);

        var previousConsoleUid = core.ConsoleUid;

        foreach (var (compType, setField) in _singleComponents)
        {
            LinkSingleComponent(coreUid, core, compType, setField, coreTile, gridUid.Value, grid);
        }

        LinkCorners(coreUid, core, coreTile, gridUid.Value, grid);

        if (core.ConsoleUid != null && EntityManager.TryGetComponent<HFRConsoleComponent>(core.ConsoleUid, out var consoleComp))
        {
            _hfrConsoleSystem.SetPowerState(core.ConsoleUid.Value, consoleComp);
        }
        else if (previousConsoleUid != null && EntityManager.TryGetComponent<HFRConsoleComponent>(previousConsoleUid, out var prevConsoleComp))
        {
            _hfrConsoleSystem.SetPowerState(previousConsoleUid.Value, prevConsoleComp);
        }
    }

    private void LinkSingleComponent(EntityUid coreUid, HFRCoreComponent core, Type compType, Action<HFRCoreComponent, EntityUid?> setField, Vector2i coreTile, EntityUid gridUid, MapGridComponent grid)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        foreach (var offset in CardinalOffsets)
        {
            var targetTile = coreTile + offset;

            if (compType == typeof(HFRConsoleComponent))
            {
                var entities = new HashSet<Entity<HFRConsoleComponent>>();
                _lookupSystem.GetLocalEntitiesIntersecting(gridUid, targetTile, entities);
                foreach (var (entity, comp) in entities)
                {
                    if (xformQuery.TryGetComponent(entity, out var compXform) && compXform.Anchored)
                    {
                        var compRotation = compXform.LocalRotation;
                        var compDir = compRotation.GetCardinalDir();
                        var expectedCoreOffset = compDir.ToIntVec();
                        if (offset == expectedCoreOffset)
                        {
                            setField(core, entity);
                            comp.CoreUid = coreUid;
                            break;
                        }
                    }
                }
            }
            else if (compType == typeof(HFRFuelInputComponent))
            {
                var entities = new HashSet<Entity<HFRFuelInputComponent>>();
                _lookupSystem.GetLocalEntitiesIntersecting(gridUid, targetTile, entities);
                foreach (var (entity, comp) in entities)
                {
                    if (xformQuery.TryGetComponent(entity, out var compXform) && compXform.Anchored)
                    {
                        var compRotation = compXform.LocalRotation;
                        var compDir = compRotation.GetCardinalDir();
                        var expectedCoreOffset = compDir.ToIntVec();
                        if (offset == expectedCoreOffset)
                        {
                            setField(core, entity);
                            comp.CoreUid = coreUid;
                            break;
                        }
                    }
                }
            }
            else if (compType == typeof(HFRModeratorInputComponent))
            {
                var entities = new HashSet<Entity<HFRModeratorInputComponent>>();
                _lookupSystem.GetLocalEntitiesIntersecting(gridUid, targetTile, entities);
                foreach (var (entity, comp) in entities)
                {
                    if (xformQuery.TryGetComponent(entity, out var compXform) && compXform.Anchored)
                    {
                        var compRotation = compXform.LocalRotation;
                        var compDir = compRotation.GetCardinalDir();
                        var expectedCoreOffset = compDir.ToIntVec();
                        if (offset == expectedCoreOffset)
                        {
                            setField(core, entity);
                            comp.CoreUid = coreUid;
                            break;
                        }
                    }
                }
            }
            else if (compType == typeof(HFRWasteOutputComponent))
            {
                var entities = new HashSet<Entity<HFRWasteOutputComponent>>();
                _lookupSystem.GetLocalEntitiesIntersecting(gridUid, targetTile, entities);
                foreach (var (entity, comp) in entities)
                {
                    if (xformQuery.TryGetComponent(entity, out var compXform) && compXform.Anchored)
                    {
                        var compRotation = compXform.LocalRotation;
                        var compDir = compRotation.GetCardinalDir();
                        var expectedCoreOffset = compDir.ToIntVec();
                        if (offset == expectedCoreOffset)
                        {
                            setField(core, entity);
                            comp.CoreUid = coreUid;
                            break;
                        }
                    }
                }
            }
        }
    }

    private void LinkCorners(EntityUid coreUid, HFRCoreComponent core, Vector2i coreTile, EntityUid gridUid, MapGridComponent grid)
    {
        core.CornerUids.Clear();
        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var offset in DiagonalOffsets)
        {
            var targetTile = coreTile + offset;
            var entities = new HashSet<Entity<HFRCornerComponent>>();
            _lookupSystem.GetLocalEntitiesIntersecting(gridUid, targetTile, entities);

            foreach (var (entity, cornerComp) in entities)
            {
                if (xformQuery.TryGetComponent(entity, out var cornerXform) && cornerXform.Anchored)
                {
                    var cornerRotation = cornerXform.LocalRotation;
                    var cornerDir = cornerRotation.GetCardinalDir();

                    // Find expected direction based on offset. Only the first matching corner per
                    // diagonal is linked, so a duplicate on one diagonal cannot stand in for a
                    // missing corner elsewhere (AreAllPartsConnected now verifies the geometry too).
                    var expectedDir = GetExpectedCornerDirection(offset);
                    if (cornerDir == expectedDir && !core.CornerUids.Contains(entity))
                    {
                        cornerComp.CoreUid = coreUid;
                        core.CornerUids.Add(entity);
                        break;
                    }
                }
            }
        }
    }

    private Direction GetExpectedCornerDirection(Vector2i offset)
    {
        // Bottom-right
        if (offset == new Vector2i(1, -1))
            return Direction.South;
        // Top-left
        if (offset == new Vector2i(-1, 1))
            return Direction.North;
        // Top-right
        if (offset == new Vector2i(1, 1))
            return Direction.East;
        // Bottom-left
        if (offset == new Vector2i(-1, -1))
            return Direction.West;

        return Direction.Invalid;
    }
}