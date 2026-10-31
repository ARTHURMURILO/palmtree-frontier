// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._PS.Ships;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Radiation.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Drives the RBMK control console.
/// </summary>
/// <remarks>
///     The DM original had four computers: rods (the only one with a setpoint), stats, fuel
///     management and pump signalling (spec §G). The setpoint is <c>desired_k</c> in [0, 3], and the
///     rod percentage shown to the engineer is derived from it - <c>100 - desired_k / 3 * 100</c> -
///     rather than stored, so the window can never disagree with the physics.
/// </remarks>
public sealed class RbmkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ShipEventSystem _shipEvent = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>
    ///     Fuel handling is refused above this power percentage, exactly as the DM fuel console did
    ///     ("You cannot remove fuel from [reactor] when it is above 20% power").
    /// </summary>
    public const float MaxPowerForFuelHandling = 20f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RbmkConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RbmkConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<RbmkConsoleComponent, RbmkConsoleSetTargetMessage>(OnSetTarget);
        SubscribeLocalEvent<RbmkConsoleComponent, RbmkConsoleEjectRodMessage>(OnEjectRod);
    }

    private void OnMapInit(EntityUid uid, RbmkConsoleComponent console, MapInitEvent args)
    {
        ResolveReactor(uid, console);
    }

    private void OnUiOpened(EntityUid uid, RbmkConsoleComponent console, BoundUIOpenedEvent args)
    {
        // A console built after the reactor, or one whose reactor was moved, gets another chance.
        if (console.Reactor is null)
            ResolveReactor(uid, console);

        UpdateUi(uid, console);
    }

    /// <summary>
    ///     Find the reactor this console should drive. Never leaves the console's own grid: a
    ///     console aboard one ship must not reach the reactor of another flying in formation.
    /// </summary>
    public void ResolveReactor(EntityUid uid, RbmkConsoleComponent console)
    {
        if (console.Reactor is { } current)
        {
            if (!Deleted(current) && HasComp<RbmkReactorComponent>(current))
                return;

            console.Reactor = null;
        }

        if (!_shipEvent.TryGetGrid(uid, out var grid))
            return;

        var consoleXform = Transform(uid);
        var bestDist = float.MaxValue;
        EntityUid? best = null;

        var reactors = EntityQueryEnumerator<RbmkReactorComponent, TransformComponent>();
        while (reactors.MoveNext(out var reactorUid, out var reactor, out var reactorXform))
        {
            if (reactorXform.GridUid != grid)
                continue;

            if (console.ConsoleId != null && reactor.ConsoleId != console.ConsoleId)
                continue;

            var dist = (reactorXform.LocalPosition - consoleXform.LocalPosition).LengthSquared();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = reactorUid;
        }

        console.Reactor = best;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var consoles = EntityQueryEnumerator<RbmkConsoleComponent>();

        while (consoles.MoveNext(out var uid, out var console))
        {
            // The graphs are sampled on their own clock so they keep a steady one-point-per-second
            // history even though the window itself refreshes several times a second.
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

            if (!_ui.IsUiOpen(uid, RbmkConsoleUiKey.Key))
                continue;

            UpdateUi(uid, console);
        }
    }

    /// <summary>
    ///     Push one point onto each statistics graph, trimmed to <see cref="RbmkConsoleComponent.HistoryLength"/>.
    /// </summary>
    private void Sample(RbmkConsoleComponent console)
    {
        if (console.Reactor is not { } reactorUid ||
            !TryComp<RbmkReactorComponent>(reactorUid, out var reactor) ||
            Deleted(reactorUid))
        {
            return;
        }

        Push(console.PowerHistory, reactor.Power);
        Push(console.PsiHistory, reactor.Pressure);
        Push(console.CoolantInHistory, reactor.LastCoolantTemperature);
        Push(console.CoolantOutHistory, reactor.LastOutputTemperature);

        void Push(List<float> history, float value)
        {
            history.Add(value);

            while (history.Count > console.HistoryLength)
                history.RemoveAt(0);
        }
    }

    public void UpdateUi(EntityUid uid, RbmkConsoleComponent console)
    {
        if (!_ui.HasUi(uid, RbmkConsoleUiKey.Key))
            return;

        _ui.SetUiState(uid, RbmkConsoleUiKey.Key, GetUiState(console));
    }

    /// <summary>Raise a short-lived notice in the window.</summary>
    private void Notice(RbmkConsoleComponent console, string message)
    {
        console.Notice = message;
        console.NoticeUntil = _timing.CurTime + console.NoticeLifetime;
    }

    private RbmkConsoleBoundUserInterfaceState GetUiState(RbmkConsoleComponent console)
    {
        var state = new RbmkConsoleBoundUserInterfaceState
        {
            HasReactor = false,
            Rods = new List<RbmkConsoleFuelRod>(),
        };

        if (console.Reactor is not { } reactorUid ||
            !TryComp<RbmkReactorComponent>(reactorUid, out var reactor) ||
            Deleted(reactorUid))
        {
            return state;
        }

        state.HasReactor = true;
        state.Running = reactor.IsRunning;
        state.Slagged = reactor.Slagged;
        state.K = reactor.K;
        state.DesiredK = reactor.DesiredK;
        state.RodPercent = 100f - reactor.DesiredK / MathF.Max(reactor.MaxCriticality, 0.01f) * 100f;
        state.Temperature = reactor.Temperature;
        state.Pressure = reactor.Pressure;
        state.Power = reactor.Power;
        state.VesselIntegrity = reactor.VesselIntegrity;
        state.MaxVesselIntegrity = reactor.MaxVesselIntegrity;
        state.IntegrityCause = reactor.LastIntegrityCause;
        state.IntegrityDamage = reactor.LastIntegrityDamage;
        state.CoolantSatisfaction = reactor.CoolantSatisfaction;
        state.CoolantIn = reactor.LastCoolantTemperature;
        state.CoolantOut = reactor.LastOutputTemperature;
        state.FuelRods = reactor.FuelRods.Count;
        state.MaxFuelRods = reactor.MaxFuelRods;
        state.TemperatureOperating = reactor.TemperatureOperating;
        state.TemperatureCritical = reactor.TemperatureCritical;
        state.TemperatureMeltdown = reactor.TemperatureMeltdown;
        state.PressureCritical = reactor.PressureCritical;
        state.Notice = console.Notice;
        state.PowerHistory = new List<float>(console.PowerHistory);
        state.PsiHistory = new List<float>(console.PsiHistory);
        state.CoolantInHistory = new List<float>(console.CoolantInHistory);
        state.CoolantOutHistory = new List<float>(console.CoolantOutHistory);

        if (TryComp<RadiationSourceComponent>(reactorUid, out var source))
            state.Radiation = source.Intensity;

        for (var i = 0; i < reactor.FuelRods.Count; i++)
        {
            var rodUid = reactor.FuelRods[i];

            if (!TryComp<RbmkFuelRodComponent>(rodUid, out var rod))
                continue;

            state.Rods.Add(new RbmkConsoleFuelRod
            {
                Index = i,
                Name = MetaData(rodUid).EntityName,
                FuelPower = rod.FuelPower,
                OriginalFuelPower = rod.OriginalFuelPower,
                Depletion = rod.Depletion,
                DepletionThreshold = rod.DepletionThreshold,
            });
        }

        return state;
    }

    /// <summary>§G.1: the one write the rod console had, <c>desired_k = clamp(input, 0, 3)</c>.</summary>
    private void OnSetTarget(EntityUid uid, RbmkConsoleComponent console, RbmkConsoleSetTargetMessage args)
    {
        if (console.Reactor is not { } reactorUid ||
            !TryComp<RbmkReactorComponent>(reactorUid, out var reactor))
        {
            return;
        }

        // Math.Clamp passes NaN through; a malformed client message would then sit in DesiredK and
        // poison the UI maths forever.
        if (!float.IsFinite(args.Target))
            return;

        var max = reactor.MaxCriticality;
        reactor.DesiredK = Math.Clamp(args.Target, 0f, max);
        _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        UpdateUi(uid, console);
    }

    /// <summary>§G.3: eject a fuel rod onto the deck, refused above 20 % power.</summary>
    private void OnEjectRod(EntityUid uid, RbmkConsoleComponent console, RbmkConsoleEjectRodMessage args)
    {
        if (console.Reactor is not { } reactorUid ||
            !TryComp<RbmkReactorComponent>(reactorUid, out var reactor))
        {
            return;
        }

        if (reactor.Power > MaxPowerForFuelHandling || reactor.Slagged)
        {
            _audio.PlayPvs("/Audio/Machines/airlock_deny.ogg", uid);
            Notice(console, Loc.GetString("rbmk-console-eject-too-hot"));
            UpdateUi(uid, console);
            return;
        }

        if (args.Index < 0 || args.Index >= reactor.FuelRods.Count)
            return;

        var rodUid = reactor.FuelRods[args.Index];

        // rbmk.dm:693 - the rod is forceMove()d to `get_turf(reactor)`, i.e. it pops out of the
        // machine itself, not out of whichever console the engineer pressed. Removing it from the
        // container also unregisters it from the core.
        if (Exists(rodUid))
        {
            var fuelContainer = _container.EnsureContainer<Container>(reactorUid, RbmkReactorComponent.FuelContainer);
            _container.Remove(rodUid, fuelContainer);
            _transform.SetCoordinates(rodUid, Transform(reactorUid).Coordinates);

            // rbmk.dm:692 - switch click at the console, crane clank at the reactor.
            if (reactor.SwitchSounds.Count > 0)
                _audio.PlayPvs(_random.Pick(reactor.SwitchSounds), uid);
        }

        _audio.PlayPvs("/Audio/Machines/beep.ogg", uid);
        UpdateUi(uid, console);
    }
}
