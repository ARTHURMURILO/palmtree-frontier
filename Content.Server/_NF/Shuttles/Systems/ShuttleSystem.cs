// New Frontiers - This file is licensed under AGPLv3
// Copyright (c) 2024 New Frontiers Contributors
// See AGPLv3.txt for details.
using Content.Server._NF.Radar; // Palmtree/Coyote: radar proximity alerts
using Content.Server._NF.Station.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio; // Palmtree/Coyote: radar proximity alerts
using Robust.Shared.Audio.Systems; // Palmtree/Coyote: radar proximity alerts
using Robust.Shared.Physics.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    [Dependency] private readonly RadarConsoleSystem _radarConsole = default!;
    [Dependency] private readonly RadarBlipSystem _radarBlips = default!; // Palmtree/Coyote: radar proximity alerts
    private const float SpaceFrictionStrength = 0.0075f;
    private const float DampenDampingStrength = 0.25f;
    private const float AnchorDampingStrength = 2.5f;

    // Palmtree/Coyote: radar proximity alerts
    private const float MinProximityAlertRadius = 1f;
    private const float MinProximityPingInterval = 0.25f;
    private const float MaxProximityPingInterval = 2.5f;
    private const float ProximityPingStepMeters = 30f;
    private const float ProximityInitialIntervalMultiplier = 2f;
    private static readonly TimeSpan ProximityScanInterval = TimeSpan.FromSeconds(0.2f);
    private static readonly TimeSpan ProximityInitialToPingDelay = TimeSpan.FromSeconds(0.5f);
    private static readonly SoundPathSpecifier ProximityInitialSound = new("/Audio/_Mono/Effects/Computer/beep_series1.ogg");
    private static readonly SoundPathSpecifier ProximityPingSound = new("/Audio/_Mono/Effects/Computer/short_beep2.ogg");
    // End Palmtree/Coyote

    private void NfInitialize()
    {
        SubscribeLocalEvent<ShuttleConsoleComponent, SetInertiaDampeningRequest>(OnSetInertiaDampening);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetServiceFlagsRequest>(NfSetServiceFlags);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetTargetCoordinatesRequest>(NfSetTargetCoordinates);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetHideTargetRequest>(NfSetHideTarget);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetProximityAlertRequest>(NfSetProximityAlert); // Palmtree/Coyote
        SubscribeLocalEvent<ShuttleConsoleComponent, SetProximityAlertRadiusRequest>(NfSetProximityAlertRadius); // Palmtree/Coyote
    }

    private bool SetInertiaDampening(EntityUid uid, PhysicsComponent physicsComponent, ShuttleComponent shuttleComponent, TransformComponent transform, InertiaDampeningMode mode)
    {
        if (!transform.GridUid.HasValue)
        {
            return false;
        }

        if (mode == InertiaDampeningMode.Query)
        {
            _console.RefreshShuttleConsoles(transform.GridUid.Value);
            return false;
        }

        if (!EntityManager.HasComponent<ShuttleDeedComponent>(transform.GridUid) ||
            EntityManager.HasComponent<StationDampeningComponent>(_station.GetOwningStation(transform.GridUid)))
        {
            return false;
        }

        shuttleComponent.BodyModifier = mode switch
        {
            InertiaDampeningMode.Off => SpaceFrictionStrength,
            InertiaDampeningMode.Dampen => DampenDampingStrength,
            InertiaDampeningMode.Anchor => AnchorDampingStrength,
            _ => DampenDampingStrength, // other values: default to some sane behaviour (assume normal dampening)
        };

        if (shuttleComponent.DampingModifier != 0)
            shuttleComponent.DampingModifier = shuttleComponent.BodyModifier;
        _console.RefreshShuttleConsoles(transform.GridUid.Value);
        return true;
    }

    private void OnSetInertiaDampening(EntityUid uid, ShuttleConsoleComponent component, SetInertiaDampeningRequest args)
    {
        // Ensure that the entity requested is a valid shuttle (stations should not be togglable)
        if (!EntityManager.TryGetComponent(uid, out TransformComponent? transform) ||
            !transform.GridUid.HasValue ||
            !EntityManager.TryGetComponent(transform.GridUid, out PhysicsComponent? physicsComponent) ||
            !EntityManager.TryGetComponent(transform.GridUid, out ShuttleComponent? shuttleComponent))
        {
            return;
        }

        if (SetInertiaDampening(uid, physicsComponent, shuttleComponent, transform, args.Mode) && args.Mode != InertiaDampeningMode.Query)
            component.DampeningMode = args.Mode;
    }

    public InertiaDampeningMode NfGetInertiaDampeningMode(EntityUid entity)
    {
        if (!EntityManager.TryGetComponent<TransformComponent>(entity, out var xform))
            return InertiaDampeningMode.Dampen;

        // Not a shuttle, shouldn't be togglable
        if (!EntityManager.HasComponent<ShuttleDeedComponent>(xform.GridUid) ||
            EntityManager.HasComponent<StationDampeningComponent>(_station.GetOwningStation(xform.GridUid)))
            return InertiaDampeningMode.Station;

        if (!EntityManager.TryGetComponent(xform.GridUid, out ShuttleComponent? shuttle))
            return InertiaDampeningMode.Dampen;

        if (shuttle.BodyModifier >= AnchorDampingStrength)
            return InertiaDampeningMode.Anchor;
        else if (shuttle.BodyModifier <= SpaceFrictionStrength)
            return InertiaDampeningMode.Off;
        else
            return InertiaDampeningMode.Dampen;
    }

    public void NfSetPowered(EntityUid uid, ShuttleConsoleComponent component, bool powered)
    {
        // Ensure that the entity requested is a valid shuttle (stations should not be togglable)
        if (!EntityManager.TryGetComponent(uid, out TransformComponent? transform) ||
            !transform.GridUid.HasValue ||
            !EntityManager.TryGetComponent(transform.GridUid, out PhysicsComponent? physicsComponent) ||
            !EntityManager.TryGetComponent(transform.GridUid, out ShuttleComponent? shuttleComponent))
        {
            return;
        }

        // Update dampening physics without adjusting requested mode.
        if (!powered)
        {
            SetInertiaDampening(uid, physicsComponent, shuttleComponent, transform, InertiaDampeningMode.Anchor);
        }
        else
        {
            // Update our dampening mode if we need to, and if we aren't a station.
            var currentDampening = NfGetInertiaDampeningMode(uid);
            if (currentDampening != component.DampeningMode &&
                currentDampening != InertiaDampeningMode.Station &&
                component.DampeningMode != InertiaDampeningMode.Station)
            {
                SetInertiaDampening(uid, physicsComponent, shuttleComponent, transform, component.DampeningMode);
            }
        }
    }

    /// <summary>
    /// Get the current service flags for this grid.
    /// </summary>
    public ServiceFlags NfGetServiceFlags(EntityUid uid)
    {
        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return ServiceFlags.None;

        var gridUid = transform.GridUid.Value;

        // Set the service flags on the IFFComponent.
        if (!EntityManager.TryGetComponent<IFFComponent>(gridUid, out var iffComponent))
            return ServiceFlags.None;

        return iffComponent.ServiceFlags;
    }

    /// <summary>
    /// Set the service flags for this grid.
    /// </summary>
    public void NfSetServiceFlags(EntityUid uid, ShuttleConsoleComponent component, SetServiceFlagsRequest args)
    {
        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return;

        var gridUid = transform.GridUid.Value;

        // Set the service flags on the IFFComponent.
        if (!EntityManager.TryGetComponent<IFFComponent>(gridUid, out var iffComponent))
            return;

        iffComponent.ServiceFlags = args.ServiceFlags;
        _console.RefreshShuttleConsoles(gridUid);
        Dirty(gridUid, iffComponent);
    }

    public void NfSetTargetCoordinates(EntityUid uid, ShuttleConsoleComponent component, SetTargetCoordinatesRequest args)
    {
        if (!TryComp<RadarConsoleComponent>(uid, out var radarConsole))
            return;

        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return;

        var gridUid = transform.GridUid.Value;

        _radarConsole.SetTarget((uid, radarConsole), args.TrackedEntity, args.TrackedPosition);
        _radarConsole.SetHideTarget((uid, radarConsole), false); // Force target visibility
        _console.RefreshShuttleConsoles(gridUid);
    }

    public void NfSetHideTarget(EntityUid uid, ShuttleConsoleComponent component, SetHideTargetRequest args)
    {
        if (!TryComp<RadarConsoleComponent>(uid, out var radarConsole))
            return;

        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return;

        var gridUid = transform.GridUid.Value;

        _radarConsole.SetHideTarget((uid, radarConsole), args.Hidden);
        _console.RefreshShuttleConsoles(gridUid);
    }

    // Palmtree/Coyote: radar proximity alerts
    public void NfSetProximityAlert(EntityUid uid, ShuttleConsoleComponent component, SetProximityAlertRequest args)
    {
        var transform = Transform(uid);
        if (!transform.GridUid.HasValue)
            return;

        component.ProximityAlertEnabled = args.Enabled;
        component.NextProximityAlert = TimeSpan.Zero;
        component.NextProximityScan = TimeSpan.Zero;
        component.HasProximityContact = false;
        component.CachedNearestProximityDistance = float.MaxValue;
        Dirty(uid, component);
        _console.RefreshShuttleConsoles(transform.GridUid.Value);
    }

    public void NfSetProximityAlertRadius(EntityUid uid, ShuttleConsoleComponent component, SetProximityAlertRadiusRequest args)
    {
        var transform = Transform(uid);
        if (!transform.GridUid.HasValue)
            return;

        // Radius is locked while active to avoid unstable timing updates from rapid slider changes.
        if (component.ProximityAlertEnabled)
            return;

        component.ProximityAlertRadius = MathF.Max(MinProximityAlertRadius, args.Radius);
        component.NextProximityAlert = TimeSpan.Zero;
        component.NextProximityScan = TimeSpan.Zero;
        component.HasProximityContact = false;
        component.CachedNearestProximityDistance = float.MaxValue;
        Dirty(uid, component);
        _console.RefreshShuttleConsoles(transform.GridUid.Value);
    }

    private void NfUpdateProximityAlerts()
    {
        var curTime = _gameTiming.CurTime;
        var consoleQuery = EntityQueryEnumerator<ShuttleConsoleComponent, RadarConsoleComponent>();

        while (consoleQuery.MoveNext(out var uid, out var console, out var radar))
        {
            // Keep alarm behavior active while enabled, even if nobody is currently using the console UI.
            if (!console.ProximityAlertEnabled)
                continue;

            if (console.NextProximityScan == TimeSpan.Zero || curTime >= console.NextProximityScan)
            {
                console.NextProximityScan = curTime + ProximityScanInterval;

                if (_radarBlips.TryGetNearestContactDistance((uid, radar), console.ProximityAlertRadius, out var nearestContactDistance))
                {
                    console.HasProximityContact = true;
                    console.CachedNearestProximityDistance = nearestContactDistance;
                }
                else
                {
                    console.HasProximityContact = false;
                    console.CachedNearestProximityDistance = float.MaxValue;
                    console.NextProximityAlert = TimeSpan.Zero;
                }
            }

            if (!console.HasProximityContact)
                continue;

            var nearestDistance = console.CachedNearestProximityDistance;

            if (console.NextProximityAlert == TimeSpan.Zero)
            {
                var initialParams = AudioParams.Default.WithVolume(-4f).WithMaxDistance(10f);
                _audio.PlayPvs(ProximityInitialSound, uid, initialParams);
                console.NextProximityAlert = curTime + ProximityInitialToPingDelay;
                continue;
            }

            if (console.NextProximityAlert != TimeSpan.Zero && curTime < console.NextProximityAlert)
                continue;

            var interval = CalculateProximityPingInterval(nearestDistance, console.ProximityAlertRadius);
            var audioParams = AudioParams.Default.WithVolume(-4f).WithMaxDistance(10f);
            _audio.PlayPvs(ProximityPingSound, uid, audioParams);
            console.NextProximityAlert = curTime + TimeSpan.FromSeconds(interval);
        }
    }

    private static float CalculateProximityPingInterval(float nearestDistance, float scanRadius)
    {
        _ = scanRadius; // Detection range gate is handled before this call; cadence is distance-based.

        var clampedDistance = MathF.Max(0f, nearestDistance);

        // 30m distance bands from center: each band farther away halves BPM (doubles interval).
        var distanceBands = (int) MathF.Floor(clampedDistance / ProximityPingStepMeters);
        var interval = MinProximityPingInterval * ProximityInitialIntervalMultiplier * MathF.Pow(2f, distanceBands);
        return Math.Clamp(interval, MinProximityPingInterval, MaxProximityPingInterval * ProximityInitialIntervalMultiplier);
    }
    // End Palmtree/Coyote
}
