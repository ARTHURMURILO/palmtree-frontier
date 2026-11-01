// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Goobstation.Supermatter.Components;
using Content.Shared._Goobstation.Supermatter.Consoles;
using Content.Shared._Goobstation.Supermatter.Monitor;
using Content.Shared.Atmos;
using Content.Shared.Radiation.Components;
using Robust.Server.GameObjects;
using System.Linq;

namespace Content.Server._Goobstation.Supermatter.Console.Systems;

public sealed partial class SupermatterConsoleSystem : SharedSupermatterConsoleSystem
{
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private const float UpdateTime = 1.0f;

    // Note: this data does not need to be saved
    private float _updateTimer = 0.0f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupermatterConsoleComponent, SupermatterConsoleFocusChangeMessage>(OnFocusChangedMessage);
    }

    #region Event handling

    private void OnFocusChangedMessage(EntityUid uid, SupermatterConsoleComponent component, SupermatterConsoleFocusChangeMessage args)
    {
        // Palmtree: a crystal's own window always shows that crystal; there is nothing else to focus.
        if (HasComp<SupermatterComponent>(uid))
            return;

        component.FocusSupermatter = args.FocusSupermatter;
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Closed consoles only need their screen refreshed once a second. An open UI still gets a
        // fresh state every tick, so clicking an entry does not wait for the next scan.
        _updateTimer -= frameTime;
        var refreshAll = _updateTimer <= 0f;
        if (refreshAll)
            _updateTimer += UpdateTime;

        // Keep a list of UI entries for each gridUid, in case multiple consoles stand on the same grid
        var supermatterEntriesForEachGrid = new Dictionary<EntityUid, SupermatterConsoleEntry[]>();

        var query = AllEntityQuery<SupermatterConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var entConsole, out var entXform))
        {
            if (entXform?.GridUid == null)
                continue;

            var uiOpen = _userInterfaceSystem.IsUiOpen(ent, SupermatterConsoleUiKey.Key);
            if (!refreshAll && !uiOpen)
                continue;

            var gridUid = entXform.GridUid.Value;
            SupermatterConsoleEntry[] supermatterEntries;

            // Make a list of alarm state data for all the supermatters on the grid.
            // Palmtree: a crystal's own window must not inherit the list built for a monitoring
            // console standing on the same grid, so self-inspection is never cached.
            if (HasComp<SupermatterComponent>(ent))
            {
                supermatterEntries = GetSupermatterStateData(gridUid, ent).ToArray();
            }
            else if (supermatterEntriesForEachGrid.TryGetValue(gridUid, out var cachedEntries))
            {
                supermatterEntries = cachedEntries;
            }
            else
            {
                supermatterEntries = GetSupermatterStateData(gridUid, ent).ToArray();
                supermatterEntriesForEachGrid[gridUid] = supermatterEntries;
            }

            // Determine the highest level of status for the console
            var highestStatus = SupermatterStatusType.Inactive;

            foreach (var entry in supermatterEntries)
            {
                var status = entry.EntityStatus;

                if (status > highestStatus)
                    highestStatus = status;
            }

            // Update the appearance of the console based on the highest recorded level of alert
            if (TryComp<AppearanceComponent>(ent, out var entAppearance))
                _appearance.SetData(ent, SupermatterConsoleVisuals.ComputerLayerScreen, (int)highestStatus, entAppearance);

            // If the console UI is open, send UI data to each subscribed session
            UpdateUIState(ent, supermatterEntries, entConsole, entXform);
        }
    }

    /// <summary>
    ///     Palmtree: point a console - or a crystal acting as its own console - at a supermatter.
    /// </summary>
    public void SetFocus(EntityUid consoleUid, EntityUid supermatterUid, SupermatterConsoleComponent? console = null)
    {
        if (!Resolve(consoleUid, ref console))
            return;

        console.FocusSupermatter = GetNetEntity(supermatterUid);
    }

    public void UpdateUIState
        (EntityUid uid,
        SupermatterConsoleEntry[] supermatterStateData,
        SupermatterConsoleComponent component,
        TransformComponent xform)
    {
        if (!_userInterfaceSystem.IsUiOpen(uid, SupermatterConsoleUiKey.Key))
            return;

        var gridUid = xform.GridUid!.Value;

        // Palmtree: the focused supermatter can be gone (delaminated, deleted, console re-linked).
        // GetEntity() on an invalid NetEntity yields EntityUid 0, and Transform(0) then threw
        // KeyNotFoundException on every update tick. Bail out cleanly instead.
        // A crystal acting as its own console is always focused on itself.
        var selfInspection = HasComp<SupermatterComponent>(uid);
        SupermatterFocusData? focusSupermatterData = null;

        if (selfInspection)
        {
            focusSupermatterData = GetFocusSupermatterData(uid, uid, gridUid);
        }
        else if (component.FocusSupermatter is { } focused)
        {
            var focusUid = GetEntity(focused);

            if (EntityManager.EntityExists(focusUid))
                focusSupermatterData = GetFocusSupermatterData(uid, focusUid, gridUid);
        }

        // Set the UI state
        _userInterfaceSystem.SetUiState(uid, SupermatterConsoleUiKey.Key,
            new SupermatterConsoleBoundInterfaceState(supermatterStateData, focusSupermatterData, selfInspection));
    }

    private List<SupermatterConsoleEntry> GetSupermatterStateData(EntityUid gridUid, EntityUid consoleUid)
    {
        var supermatterStateData = new List<SupermatterConsoleEntry>();

        // Palmtree: a crystal acting as its own console (admin inspection) reports only itself. A
        // monitoring console still reports every installed crystal on its own grid - and nothing
        // from the ship flying next to it.
        if (TryComp<SupermatterComponent>(consoleUid, out var selfSupermatter))
        {
            supermatterStateData.Add(MakeEntry(consoleUid, selfSupermatter));
            return supermatterStateData;
        }

        var querySupermatters = AllEntityQuery<SupermatterComponent, TransformComponent>();
        while (querySupermatters.MoveNext(out var ent, out var entSupermatter, out var entXform))
        {
            if (entXform.GridUid != gridUid
                || !entXform.Anchored)
                continue;

            supermatterStateData.Add(MakeEntry(ent, entSupermatter));
        }

        return supermatterStateData;
    }

    private SupermatterConsoleEntry MakeEntry(EntityUid uid, SupermatterComponent supermatter) =>
        new(GetNetEntity(uid),
            MetaData(uid).EntityName,
            supermatter.Status,
            supermatter.ConsoleIcon);

    private SupermatterFocusData? GetFocusSupermatterData(EntityUid uid, EntityUid? focusSupermatter, EntityUid gridUid)
    {
        if (focusSupermatter == null)
            return null;

        var focusSupermatterXform = Transform(focusSupermatter.Value);

        // Palmtree: self-inspection from an unanchored shard must still return its stats.
        if ((!focusSupermatterXform.Anchored && focusSupermatter != uid)
            || focusSupermatterXform.GridUid != gridUid
            || !TryComp<SupermatterComponent>(focusSupermatter.Value, out var sm)
            || !TryComp<RadiationSourceComponent>(focusSupermatter.Value, out var radiationComp))
            return null;

        var gases = sm.GasStorage;
        var tempThreshold = Atmospherics.T0C + sm.HeatPenaltyThreshold;

        return new SupermatterFocusData(
            GetNetEntity(focusSupermatter.Value),
            GetIntegrity(sm),
            sm.Power,
            radiationComp.Intensity,
            gases.Sum(gas => gases[gas.Key]),
            sm.Temperature,
            tempThreshold * sm.DynamicHeatResistance,
            sm.WasteMultiplier,
            sm.GasEfficiency * 100,
            sm.GasStorage);
    }

    public float GetIntegrity(SupermatterComponent sm)
    {
        var integrity = sm.Damage / sm.DelaminationPoint;
        integrity = (float)Math.Round(100 - integrity * 100, 2);
        integrity = integrity < 0 ? 0 : integrity;
        return integrity;
    }
}
