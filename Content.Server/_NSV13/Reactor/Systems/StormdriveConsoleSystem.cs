// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Content.Server._NSV13.Reactor.Components;
using Content.Server._PS.Ships;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Atmos;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Drives the stormdrive control console.
/// </summary>
/// <remarks>
///     Faithful to <c>/obj/machinery/computer/ship/reactor_control_computer</c>
///     (stormdrive.dm:1361-1440): the rod slider, the five presets (0 / 5 / 16 / 33.6 / 100 %), the
///     maintenance toggle and the fuel-dump switch, plus the full input mixture readout.
/// </remarks>
public sealed class StormdriveConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StormdriveSystem _stormdrive = default!;
    [Dependency] private readonly ShipEventSystem _shipEvent = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>
    ///     The five preset stop positions from the DM console's <c>rods_1</c>..<c>rods_5</c>.
    ///     0 is maximum reactivity, 100 is a SCRAM.
    /// </summary>
    public static readonly float[] Presets = { 0f, 5f, 16f, 33.6f, 100f };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StormdriveConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<StormdriveConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<StormdriveConsoleComponent, StormdriveConsoleSetRodsMessage>(OnSetRods);
        SubscribeLocalEvent<StormdriveConsoleComponent, StormdriveConsolePresetMessage>(OnPreset);
        SubscribeLocalEvent<StormdriveConsoleComponent, StormdriveConsoleMaintenanceMessage>(OnMaintenance);
        SubscribeLocalEvent<StormdriveConsoleComponent, StormdriveConsoleDumpMessage>(OnDump);
        SubscribeLocalEvent<StormdriveConsoleComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<StormdriveConsoleComponent, PortDisconnectedEvent>(OnPortDisconnected);
    }

    private void OnMapInit(EntityUid uid, StormdriveConsoleComponent console, MapInitEvent args)
    {
        ResolveReactor(uid, console);
    }

    private void OnUiOpened(EntityUid uid, StormdriveConsoleComponent console, BoundUIOpenedEvent args)
    {
        // ResolveReactor is a no-op while the current binding is alive, so this also recovers a
        // console whose reactor was deleted (or moved to another grid) while nobody was looking.
        ResolveReactor(uid, console);
        UpdateUi(uid, console);
    }

    /// <summary>
    ///     Bind to the stormdrive the multitool link points at.
    /// </summary>
    /// <remarks>
    ///     As with the RBMK console, proximity no longer binds anything: the link is a normal
    ///     <c>DeviceLink</c> from the console's <c>ReactorControl</c> source port to the drive's
    ///     matching sink port, which makes the binding explicit, persistent and clearable.
    /// </remarks>
    public void ResolveReactor(EntityUid uid, StormdriveConsoleComponent console)
    {
        if (console.Reactor is { } current)
        {
            if (!Deleted(current) && HasComp<StormdriveComponent>(current))
                return;

            console.Reactor = null;
        }

        if (!TryComp<DeviceLinkSourceComponent>(uid, out var links))
            return;

        foreach (var sink in links.LinkedPorts.Keys)
        {
            if (TryLinkReactor(uid, console, sink))
                return;
        }
    }

    /// <summary>
    ///     Point the console at a drive, if the target is a drive, on this console's own grid, and
    ///     matches the console's optional tag.
    /// </summary>
    /// <returns>True if the console is now bound to <paramref name="reactorUid"/>.</returns>
    public bool TryLinkReactor(EntityUid uid, StormdriveConsoleComponent console, EntityUid reactorUid)
    {
        if (!TryComp<StormdriveComponent>(reactorUid, out var reactor))
            return false;

        if (!_shipEvent.TryGetGrid(uid, out var grid) || Transform(reactorUid).GridUid != grid)
            return false;

        if (console.ConsoleId != null && reactor.ReactorId != console.ConsoleId)
            return false;

        console.Reactor = reactorUid;
        return true;
    }

    private void OnNewLink(EntityUid uid, StormdriveConsoleComponent console, NewLinkEvent args)
    {
        if (TryLinkReactor(uid, console, args.Sink))
            UpdateUi(uid, console);
    }

    private void OnPortDisconnected(EntityUid uid, StormdriveConsoleComponent console, PortDisconnectedEvent args)
    {
        // The event fires before the link is dropped from LinkedPorts, so do not re-resolve here.
        console.Reactor = null;
        UpdateUi(uid, console);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var consoles = EntityQueryEnumerator<StormdriveConsoleComponent>();

        while (consoles.MoveNext(out var uid, out var console))
        {
            if (now >= console.NextSample)
            {
                console.NextSample = now + TimeSpan.FromSeconds(console.SampleInterval);
                Sample(console);
            }

            if (now < console.NextUpdate)
                continue;

            console.NextUpdate = now + TimeSpan.FromSeconds(console.UpdatePeriod);

            if (console.Notice.Length > 0 && now >= console.NoticeUntil)
                console.Notice = string.Empty;

            if (!_ui.IsUiOpen(uid, StormdriveConsoleUiKey.Key))
                continue;

            UpdateUi(uid, console);
        }
    }

    /// <summary>
    ///     Push one composition sample per gas, trimmed to the console's history length.
    /// </summary>
    /// <remarks>
    ///     Samples are percentages of the fuel line, not raw moles. That is what the DM
    ///     <c>gas_records</c> stored and what the chart's fixed 0-100 axis draws; raw moles made
    ///     every rich mixture clip at the top of the graph and flattened the small gases to nothing.
    /// </remarks>
    private void Sample(StormdriveConsoleComponent console)
    {
        if (console.Reactor is not { } reactorUid ||
            !TryComp<StormdriveComponent>(reactorUid, out var reactor) ||
            Deleted(reactorUid))
        {
            return;
        }

        if (!_nodeContainer.TryGetNode(reactorUid, reactor.FuelInputName, out PipeNode? fuelNode))
            return;

        var air = fuelNode.Air;
        var total = air.TotalMoles;

        Span<float> values = stackalloc float[(int) StormdriveConsoleGas.Count];
        if (total > 0f)
        {
            values[(int) StormdriveConsoleGas.Plasma] = air.GetMoles(Gas.Plasma) / total * 100f;
            values[(int) StormdriveConsoleGas.Tritium] = air.GetMoles(Gas.Tritium) / total * 100f;
            values[(int) StormdriveConsoleGas.Oxygen] = air.GetMoles(Gas.Oxygen) / total * 100f;
            values[(int) StormdriveConsoleGas.Nitrogen] = air.GetMoles(Gas.Nitrogen) / total * 100f;
            values[(int) StormdriveConsoleGas.CarbonDioxide] = air.GetMoles(Gas.CarbonDioxide) / total * 100f;
            values[(int) StormdriveConsoleGas.WaterVapour] = air.GetMoles(Gas.WaterVapor) / total * 100f;
            values[(int) StormdriveConsoleGas.NitrousOxide] = air.GetMoles(Gas.NitrousOxide) / total * 100f;
            values[(int) StormdriveConsoleGas.Nucleium] = air.GetMoles(Gas.Nucleium) / total * 100f;
            values[(int) StormdriveConsoleGas.BZ] = air.GetMoles(Gas.BZ) / total * 100f;
            values[(int) StormdriveConsoleGas.Healium] = air.GetMoles(Gas.Healium) / total * 100f;
            values[(int) StormdriveConsoleGas.Nitrium] = air.GetMoles(Gas.Nitrium) / total * 100f;
            values[(int) StormdriveConsoleGas.Pluoxium] = air.GetMoles(Gas.Pluoxium) / total * 100f;
            values[(int) StormdriveConsoleGas.Hydrogen] = air.GetMoles(Gas.Hydrogen) / total * 100f;
            values[(int) StormdriveConsoleGas.HyperNoblium] = air.GetMoles(Gas.HyperNoblium) / total * 100f;
            values[(int) StormdriveConsoleGas.ProtoNitrate] = air.GetMoles(Gas.ProtoNitrate) / total * 100f;
            values[(int) StormdriveConsoleGas.Zauker] = air.GetMoles(Gas.Zauker) / total * 100f;
            values[(int) StormdriveConsoleGas.Halon] = air.GetMoles(Gas.Halon) / total * 100f;
            values[(int) StormdriveConsoleGas.AntiNoblium] = air.GetMoles(Gas.AntiNoblium) / total * 100f;
        }

        for (var i = 0; i < (int) StormdriveConsoleGas.Count; i++)
        {
            while (console.GasHistory.Count <= i)
                console.GasHistory.Add(new List<float>());

            var series = console.GasHistory[i];
            series.Add(values[i]);

            while (series.Count > console.HistoryLength)
                series.RemoveAt(0);
        }
    }

    public void UpdateUi(EntityUid uid, StormdriveConsoleComponent console)
    {
        if (!_ui.HasUi(uid, StormdriveConsoleUiKey.Key))
            return;

        _ui.SetUiState(uid, StormdriveConsoleUiKey.Key, GetUiState(uid, console));
    }

    public StormdriveConsoleBoundUserInterfaceState GetUiState(EntityUid uid, StormdriveConsoleComponent console)
    {
        var state = new StormdriveConsoleBoundUserInterfaceState
        {
            HasReactor = false,
        };

        if (console.Reactor is not { } reactorUid ||
            !TryComp<StormdriveComponent>(reactorUid, out var reactor) ||
            Deleted(reactorUid))
        {
            return state;
        }

        state.HasReactor = true;
        state.State = (StormdriveConsoleState) (int) reactor.State;
        state.Heat = reactor.Heat;
        state.RodIntegrity = Math.Clamp(reactor.ControlRodIntegrity, 0f, 100f);
        state.ReactorStability = Math.Clamp(reactor.ReactorStability, 0f, 100f);
        state.StabilityDelta = reactor.LastStabilityDelta;
        state.StabilityCause = reactor.LastStabilityCause;
        state.StabilitySpikeThreshold = reactor.StabilitySpikeThreshold;
        state.StabilityAnomalyThreshold = reactor.StabilityAnomalyThreshold;
        state.RodPercent = reactor.ControlRodPercent;
        state.PipeOpen = reactor.DumpingFuel;
        state.LastPowerProduced = reactor.LastPowerProduced;
        state.TheoreticalMaximumPower = reactor.TheoreticalMaximumPower;
        // The simulation figure is moles per process tick; the window labels it mol/s.
        state.ReactionRate = reactor.ReactionRate / MathF.Max(reactor.ProcessInterval, 0.01f);

        state.TemperatureHot = reactor.TemperatureHot;
        state.TemperatureCritical = reactor.TemperatureCritical;
        state.TemperatureMeltdown = reactor.TemperatureMeltdown;
        state.Maintenance = reactor.State == StormdriveState.Maintenance;
        state.Notice = console.Notice;

        if (reactor.WarningState == StormdriveWarning.Meltdown && reactor.MeltdownAt > _timing.CurTime)
            state.MeltdownCountdown = (float) (reactor.MeltdownAt - _timing.CurTime).TotalSeconds;

        // Always send the fuse length, not just while armed: the window needs the denominator to
        // draw the bar, and sending it unconditionally keeps the payload a plain snapshot.
        state.MeltdownDelay = (float) reactor.MeltdownDelay.TotalSeconds;

        if (!_nodeContainer.TryGetNode(reactorUid, reactor.FuelInputName, out PipeNode? fuelNode))
            return state;

        var air = fuelNode.Air;

        state.Oxygen = air.GetMoles(Gas.Oxygen);
        state.Plasma = air.GetMoles(Gas.Plasma);
        state.Tritium = air.GetMoles(Gas.Tritium);
        state.CarbonDioxide = air.GetMoles(Gas.CarbonDioxide);
        state.WaterVapor = air.GetMoles(Gas.WaterVapor);
        state.Nitrogen = air.GetMoles(Gas.Nitrogen);
        state.NitrousOxide = air.GetMoles(Gas.NitrousOxide);
        state.Nucleium = air.GetMoles(Gas.Nucleium);
        state.BZ = air.GetMoles(Gas.BZ);
        state.Healium = air.GetMoles(Gas.Healium);
        state.Nitrium = air.GetMoles(Gas.Nitrium);
        state.Pluoxium = air.GetMoles(Gas.Pluoxium);
        state.Hydrogen = air.GetMoles(Gas.Hydrogen);
        state.HyperNoblium = air.GetMoles(Gas.HyperNoblium);
        state.ProtoNitrate = air.GetMoles(Gas.ProtoNitrate);
        state.Zauker = air.GetMoles(Gas.Zauker);
        state.Halon = air.GetMoles(Gas.Halon);
        state.AntiNoblium = air.GetMoles(Gas.AntiNoblium);
        state.TotalMoles = air.TotalMoles;

        // fuel_mix is the same two positive contributors the ignition check counts in full.
        state.FuelMix = state.Plasma + state.Tritium;

        // The warning bands from the DM console: reaction-rate driven while running, fixed otherwise
        // ("Just need to avoid that inital orange").
        if (reactor.State == StormdriveState.Running)
        {
            state.MoleThresholdVeryHigh = reactor.ReactionRate * 18f + 20f;
            state.MoleThresholdHigh = reactor.ReactionRate * 12f + 20f;
        }
        else
        {
            state.MoleThresholdVeryHigh = 120f;
            state.MoleThresholdHigh = 80f;
        }

        // Deep copy: the state crosses the network boundary and the console keeps mutating its own
        // history every second.
        state.GasHistory = new List<List<float>>(console.GasHistory.Count);

        foreach (var series in console.GasHistory)
            state.GasHistory.Add(new List<float>(series));

        return state;
    }

    private bool TryGetReactor(
        EntityUid uid,
        StormdriveConsoleComponent console,
        out EntityUid reactorUid,
        [NotNullWhen(true)] out StormdriveComponent? reactor)
    {
        if (console.Reactor is { } bound && !Deleted(bound) && TryComp(bound, out reactor))
        {
            reactorUid = bound;
            return true;
        }

        // The bound reactor is gone or no longer a drive: re-run the search now, and use whatever it
        // finds, rather than making the player click again after the first no-op.
        ResolveReactor(uid, console);

        if (console.Reactor is { } resolved && !Deleted(resolved) && TryComp(resolved, out reactor))
        {
            reactorUid = resolved;
            return true;
        }

        reactorUid = default;
        reactor = null;
        return false;
    }

    /// <summary>Raise a short-lived notice in the window.</summary>
    private void Notice(StormdriveConsoleComponent console, string message)
    {
        console.Notice = message;
        console.NoticeUntil = _timing.CurTime + console.NoticeLifetime;
    }

    private void OnSetRods(EntityUid uid, StormdriveConsoleComponent console, StormdriveConsoleSetRodsMessage args)
    {
        if (!TryGetReactor(uid, console, out var reactorUid, out var reactor))
            return;

        _stormdrive.SetControlRods(reactorUid, reactor, args.Percent);
        _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        UpdateUi(uid, console);
    }

    private void OnPreset(EntityUid uid, StormdriveConsoleComponent console, StormdriveConsolePresetMessage args)
    {
        if (!TryGetReactor(uid, console, out var reactorUid, out var reactor))
            return;

        if (args.Preset < 0 || args.Preset >= Presets.Length)
            return;

        var percent = Presets[args.Preset];
        _stormdrive.SetControlRods(reactorUid, reactor, percent);

        // DM: position 100 % is "SCRAM protocols engaged. Attempting reactor shutdown!" - a shout,
        // not a quiet slider move.
        if (percent >= 100f)
        {
            _audio.PlayPvs("/Audio/Machines/alarm.ogg", uid);
            Notice(console, Loc.GetString("stormdrive-console-scram"));
        }
        else
        {
            _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        }

        UpdateUi(uid, console);
    }

    private void OnMaintenance(EntityUid uid, StormdriveConsoleComponent console, StormdriveConsoleMaintenanceMessage args)
    {
        if (!TryGetReactor(uid, console, out var reactorUid, out var reactor))
            return;

        var enabling = reactor.State != StormdriveState.Maintenance;

        if (!_stormdrive.SetMaintenance(reactorUid, reactor, enabling))
        {
            // DM: "DANGER! Maintenance protocols cannot be initiated while the reactor is active"
            _audio.PlayPvs("/Audio/Machines/airlock_deny.ogg", uid);
            Notice(console, Loc.GetString("stormdrive-console-maintenance-refused"));
        }
        else
        {
            _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        }

        UpdateUi(uid, console);
    }

    private void OnDump(EntityUid uid, StormdriveConsoleComponent console, StormdriveConsoleDumpMessage args)
    {
        if (!TryGetReactor(uid, console, out _, out var reactor))
            return;

        reactor.DumpingFuel = !reactor.DumpingFuel;
        _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        UpdateUi(uid, console);
    }
}
