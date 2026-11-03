// SPDX-FileCopyrightText: 2025 VMSolidus <evilexecutive@gmail.com>
// SPDX-FileCopyrightText: 2025 corresp0nd <46357632+corresp0nd@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Server.Power.EntitySystems;
using Content.Server.Tesla.Components;
using Content.Shared._EE.CCVars;
using Content.Shared._EE.Supermatter.Components;
using Content.Shared._EE.Supermatter.Consoles;
using Content.Shared.Atmos;
using Content.Shared.Radiation.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server._EE.Supermatter.Console.Systems;

public sealed partial class SupermatterConsoleSystem : SharedSupermatterConsoleSystem
{
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private EntityLookupSystem _entityLookup = default!;

    // Triad: gate the console refresh to 1 Hz. Upstream EE ran this Update() every
    // server tick, pushing a full BUI state (supermatter list + a whole GasMixture) to
    // every open client per tick, which thrashes the client and reads as a grid flicker.
    // The atmos monitoring console this was cloned from throttles the same way.
    private const float UpdateTime = 1.0f;
    private float _updateTimer;

    public override void Initialize()
    {
        base.Initialize();

        // Console events
        SubscribeLocalEvent<SupermatterConsoleComponent, ComponentInit>(OnConsoleInit);
        SubscribeLocalEvent<SupermatterConsoleComponent, EntParentChangedMessage>(OnConsoleParentChanged);
        SubscribeLocalEvent<SupermatterConsoleComponent, SupermatterConsoleFocusChangeMessage>(OnFocusChangedMessage);

        // Grid events
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    #region Event Handling

    private void OnConsoleInit(EntityUid uid, SupermatterConsoleComponent component, ComponentInit args)
    {
        InitalizeConsole(uid, component);
    }

    private void OnConsoleParentChanged(EntityUid uid, SupermatterConsoleComponent component, EntParentChangedMessage args)
    {
        InitalizeConsole(uid, component);
    }

    private void OnFocusChangedMessage(EntityUid uid, SupermatterConsoleComponent component, SupermatterConsoleFocusChangeMessage args)
    {
        // A crystal's own window always shows that crystal.
        if (HasComp<SupermatterComponent>(uid))
            return;

        component.FocusSupermatter = args.FocusSupermatter;
        Dirty(uid, component);
    }

    /// <summary>
    /// Palmtree: point a console - or a crystal acting as its own console - at a crystal.
    /// </summary>
    public void SetFocus(EntityUid consoleUid, EntityUid supermatterUid, SupermatterConsoleComponent? console = null)
    {
        if (!Resolve(consoleUid, ref console))
            return;

        console.FocusSupermatter = GetNetEntity(supermatterUid);
        Dirty(consoleUid, console);
    }

    /// <summary>
    /// Palmtree: pick a monitoring console on the crystal's grid to broadcast engine alarms from.
    /// </summary>
    /// <remarks>
    /// The crystal's own <see cref="SupermatterConsoleComponent"/> (used for admin self-inspection)
    /// is never a candidate, and neither is anything else that is itself a crystal. Unpowered
    /// consoles are skipped as well. If the ship has no powered monitoring console, the alarm is
    /// simply not broadcast. A console is only a sender here: it has no radio receiver or speaker,
    /// so it does not repeat Shortband traffic.
    /// </remarks>
    public bool TryGetAnnouncingConsole(EntityUid crystal, out EntityUid console)
    {
        console = default;

        if (!TryComp(crystal, out TransformComponent? crystalXform) ||
            crystalXform.GridUid is not { } gridUid)
        {
            return false;
        }

        var candidates = new List<EntityUid>();
        var query = AllEntityQuery<SupermatterConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out _, out var xform))
        {
            if (ent == crystal || HasComp<SupermatterComponent>(ent))
                continue;

            if (xform.GridUid != gridUid || !_power.IsPowered(ent))
                continue;

            candidates.Add(ent);
        }

        if (candidates.Count == 0)
            return false;

        console = _random.Pick(candidates);
        return true;
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        // Collect grids
        var allGrids = args.NewGrids.ToList();

        if (!allGrids.Contains(args.Grid))
            allGrids.Add(args.Grid);

        // Update supermatter monitoring consoles that stand on an updated grid
        var query = AllEntityQuery<SupermatterConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var entConsole, out var entXform))
        {
            if (entXform.GridUid == null)
                continue;

            if (!allGrids.Contains(entXform.GridUid.Value))
                continue;

            InitalizeConsole(ent, entConsole);
        }
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Triad: refresh at 1 Hz instead of every tick (see field comment above).
        _updateTimer += frameTime;
        if (_updateTimer < UpdateTime)
            return;

        _updateTimer -= UpdateTime;

        // Keep a list of UI entries for each gridUid, in case multiple consoles stand on the same grid
        var supermatterEntriesForEachGrid = new Dictionary<EntityUid, SupermatterConsoleEntry[]>();

        var query = AllEntityQuery<SupermatterConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var entConsole, out var entXform))
        {
            if (entXform?.GridUid == null)
                continue;

            // A crystal acting as its own console must not inherit the list cached for a
            // monitoring console standing on the same grid.
            SupermatterConsoleEntry[] supermatterEntries;
            if (HasComp<SupermatterComponent>(ent))
            {
                supermatterEntries = GetSupermatterStateData(entXform.GridUid.Value, ent).ToArray();
            }
            else if (supermatterEntriesForEachGrid.TryGetValue(entXform.GridUid.Value, out var cachedEntries))
            {
                supermatterEntries = cachedEntries;
            }
            else
            {
                supermatterEntries = GetSupermatterStateData(entXform.GridUid.Value, ent).ToArray();
                supermatterEntriesForEachGrid[entXform.GridUid.Value] = supermatterEntries;
            }

            // Determine the highest level of supermatter status for the console
            var highestStatus = SupermatterStatusType.Inactive;

            foreach (var entry in supermatterEntries)
            {
                var status = entry.EntityStatus;

                if (status > highestStatus)
                    highestStatus = status;
            }

            // Update the appearance of the console based on the highest recorded level of supermatter status
            if (TryComp<AppearanceComponent>(ent, out var entAppearance))
                _appearance.SetData(ent, SupermatterConsoleVisuals.ComputerLayerScreen, (int)highestStatus, entAppearance);

            // If the console UI is open, send UI data to each subscribed session
            UpdateUIState(ent, supermatterEntries, entConsole, entXform);
        }
    }

    private void UpdateUIState(EntityUid uid, SupermatterConsoleEntry[] supermatterStateData, SupermatterConsoleComponent component, TransformComponent xform)
    {
        if (!_userInterfaceSystem.IsUiOpen(uid, SupermatterConsoleUiKey.Key))
            return;

        var gridUid = xform.GridUid!.Value;

        // A crystal's own window is always focused on itself; a monitoring console uses the
        // focus selected by its UI.
        var selfInspection = HasComp<SupermatterComponent>(uid);
        var focusUid = selfInspection ? uid : GetEntity(component.FocusSupermatter);
        var focusSupermatterData = GetFocusSupermatterData(focusUid, gridUid, selfInspection);

        // Set the UI state
        _userInterfaceSystem.SetUiState(uid, SupermatterConsoleUiKey.Key,
            new SupermatterConsoleBoundInterfaceState(supermatterStateData, focusSupermatterData, selfInspection));
    }

    private List<SupermatterConsoleEntry> GetSupermatterStateData(EntityUid gridUid, EntityUid consoleUid)
    {
        var supermatterStateData = new List<SupermatterConsoleEntry>();

        // A crystal acting as its own console reports only itself.
        if (TryComp<SupermatterComponent>(consoleUid, out var selfSupermatter))
        {
            supermatterStateData.Add(new SupermatterConsoleEntry(
                GetNetEntity(consoleUid),
                MetaData(consoleUid).EntityName,
                selfSupermatter.Status,
                selfSupermatter.ConsoleIcon));
            return supermatterStateData;
        }

        var querySupermatters = AllEntityQuery<SupermatterComponent, TransformComponent>();
        while (querySupermatters.MoveNext(out var ent, out var entSupermatter, out var entXform))
        {
            if (entXform.GridUid != gridUid)
                continue;

            if (!entXform.Anchored)
                continue;

            // Create entry
            var netEnt = GetNetEntity(ent);

            var entry = new SupermatterConsoleEntry(
                netEnt,
                MetaData(ent).EntityName,
                entSupermatter.Status,
                entSupermatter.ConsoleIcon);

            supermatterStateData.Add(entry);
        }

        return supermatterStateData;
    }

    private SupermatterFocusData? GetFocusSupermatterData(EntityUid? focusSupermatter, EntityUid gridUid, bool selfInspection = false)
    {
        if (focusSupermatter == null)
            return null;

        if (!TryComp(focusSupermatter.Value, out TransformComponent? focusSupermatterXform))
            return null;

        if (!selfInspection &&
            (!focusSupermatterXform.Anchored ||
             focusSupermatterXform.GridUid != gridUid))
            return null;

        if (!TryComp<SupermatterComponent>(focusSupermatter.Value, out var sm))
            return null;

        var gases = GasMixture.SpaceGas;
        if (sm.GasStorage != null)
            gases = sm.GasStorage;

        var tempThreshold = Atmospherics.T0C + _config.GetCVar(ECCVars.SupermatterHeatPenaltyThreshold);
        var effectivePower = sm.Power * sm.LightningTransmission;
        var lightningRange = Math.Clamp(effectivePower / 1000f, 2f,
            _config.GetCVar(ECCVars.SupermatterLightningRange));
        var teslaCoils = _entityLookup
            .GetEntitiesInRange<TeslaCoilComponent>(focusSupermatterXform.Coordinates, lightningRange)
            .Count;
        var radiation = TryComp<RadiationSourceComponent>(focusSupermatter.Value, out var rad)
            ? rad.Intensity
            : 0f;

        return new SupermatterFocusData(
            GetNetEntity(focusSupermatter.Value),
            gases,
            GetIntegrity(sm),
            GetHeatHealing(sm),
            sm.Power,
            sm.PowerLoss,
            teslaCoils,
            lightningRange,
            sm.LightningTransmission,
            radiation,
            tempThreshold * sm.DynamicHeatResistance,
            sm.HeatModifier,
            sm.GasHeatModifier,
            sm.GasEfficiency * 100);
    }

    private static float GetIntegrity(SupermatterComponent sm)
    {
        var integrity = sm.Damage / sm.DamageDelaminationPoint;
        integrity = (float)Math.Round(100 - integrity * 100, 2);
        integrity = integrity < 0 ? 0 : integrity;
        return integrity;
    }

    private static float GetHeatHealing(SupermatterComponent sm)
    {
        var heatHealing = sm.HeatHealing / sm.DamageDelaminationPoint;
        heatHealing = (float)Math.Round(heatHealing * 100, 2);
        heatHealing = heatHealing > 0 ? 0 : heatHealing;
        return heatHealing;
    }

    private void InitalizeConsole(EntityUid uid, SupermatterConsoleComponent component)
    {
        var xform = Transform(uid);

        if (xform.GridUid == null)
            return;

        Dirty(uid, component);
    }
}
