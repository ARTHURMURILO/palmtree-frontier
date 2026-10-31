// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Content.Server.Chat.Managers;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Light.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Server.Stunnable;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Camera;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Ships;

/// <summary>
///     Ship-scoped event helpers: work out which vessel an entity belongs to, then deliver the
///     consequences to that vessel and to nothing else.
/// </summary>
/// <remarks>
///     <para>
///     Palmtree has no station. Everything a crew owns is a grid, and players are free to own
///     several at once. NSV13's originals reached for station-wide equivalents — <c>shares_overmap()</c>,
///     weather, a global APC list — none of which exist here. Every helper below resolves the
///     source entity's <see cref="TransformComponent.GridUid"/> first and then only ever touches
///     entities on that grid, so a reactor failure on one ship cannot reach a ship flying
///     alongside it, a trade post, or a safe zone.
///     </para>
///     <para>
///     Kept deliberately generic: the supermatter, the RBMK and the Stormdrive all need to say
///     "vessel X is in trouble" and make the ship feel it.
///     </para>
/// </remarks>
public sealed class ShipEventSystem : EntitySystem
{
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly PoweredLightSystem _poweredLight = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;

    /// <summary>Default alarm channel. The supermatter uses the same one.</summary>
    public static readonly ProtoId<RadioChannelPrototype> DefaultChannel = "Common";

    /// <summary>
    ///     Channel every engine alarm is broadcast on: the Shortband (<c>Traffic</c>).
    /// </summary>
    /// <remarks>
    ///     An engine failure is a problem for the ship it happens on, not a sector-wide event. The
    ///     Shortband is the fork's range-degraded channel: a neighbouring vessel hears it, garbled
    ///     with distance, and anything further out does not hear it at all. Letting the reactors
    ///     shout on Common instead meant every trade post and every other crew in the sector got the
    ///     same alarm line, which is why the engines no longer do.
    /// </remarks>
    public static readonly ProtoId<RadioChannelPrototype> EngineChannel = "Traffic";

    /// <summary>
    ///     Minimum gap between two routine alarms from the same machine, matching NSV13's
    ///     <c>alert_cooldown = 20 SECONDS</c>.
    /// </summary>
    /// <remarks>
    ///     The DM original throttled its radio chatter because a reactor in trouble calls its alert
    ///     proc from the tick loop: a failing core re-evaluates every two seconds and screams every
    ///     time. <see cref="TryAnnounce"/> reproduces that gate; genuinely new information (the
    ///     core has started melting) passes <c>urgent: true</c> and is never dropped.
    /// </remarks>
    public static readonly TimeSpan DefaultAlertCooldown = TimeSpan.FromSeconds(20);

    /// <summary>How long a blown light keeps strobing before it settles back to a steady (if dim) glow.</summary>
    public static readonly TimeSpan DefaultFlickerTime = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Resolve the grid an entity belongs to. Anchored machinery, mobs and loose items all
    ///     resolve to the ship they are standing on.
    /// </summary>
    public bool TryGetGrid(EntityUid uid, out EntityUid grid)
    {
        grid = default;

        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        if (xform.GridUid is not { } gridUid || !HasComp<MapGridComponent>(gridUid))
            return false;

        grid = gridUid;
        return true;
    }

    /// <summary>
    ///     Best-effort display name for the vessel an entity is standing on.
    /// </summary>
    /// <remarks>
    ///     Deeds live on the captain's ID card and point at the grid they were purchased for, so
    ///     the lookup runs id-card -> grid. Only <em>purchased</em> ships have one; anything
    ///     spawned by an admin or placed by a map falls through to the grid's own name, and
    ///     finally to a generic "unregistered" string. The alarm must never fail to announce
    ///     just because nobody filed the paperwork.
    /// </remarks>
    public string GetShipName(EntityUid uid)
    {
        if (!TryGetGrid(uid, out var grid))
            return Loc.GetString("ship-event-name-unknown");

        var deeds = EntityQueryEnumerator<ShuttleDeedComponent>();
        while (deeds.MoveNext(out _, out var deed))
        {
            if (deed.ShuttleUid != grid)
                continue;

            var full = ShipyardSystem.GetFullName(deed);
            if (!string.IsNullOrWhiteSpace(full))
                return full;
        }

        var name = MetaData(grid).EntityName;
        if (!string.IsNullOrWhiteSpace(name) && name != "Unknown")
            return name;

        return Loc.GetString("ship-event-name-unknown");
    }

    /// <summary>
    ///     Send a ship-identified alarm: over the radio, and directly to everyone aboard who is not
    ///     already listening to that channel.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Radio is the right channel for a distress call - a neighbouring ship overhearing it is
    ///     exactly the emergent gameplay we want. But radio needs a headset tuned to the channel,
    ///     and an engine alarm must never be silent because the engineer took their ears off, so
    ///     anyone on the grid who cannot hear the radio gets the same text straight to chat.
    ///     </para>
    ///     <para>
    ///     The two paths are mutually exclusive per player. Sending both - which an earlier version
    ///     did - shows every crew member the line twice, which reads as spam even when the alarm is
    ///     a legitimate one-off.
    ///     </para>
    /// </remarks>
    public void Announce(EntityUid source, string message, ProtoId<RadioChannelPrototype>? channel = null)
    {
        ProtoId<RadioChannelPrototype> channelId = channel ?? DefaultChannel;
        var text = Loc.GetString("ship-event-announcement", ("ship", GetShipName(source)), ("message", message));

        _radio.SendRadioMessage(source, text, channelId, source);

        if (!TryGetGrid(source, out var grid))
            return;

        // RadioSystem drops short-range traffic unless a powered telecomms server carries the channel
        // on this map. That check is private, so it is mirrored here: without it the fallback below
        // would consider every headset-wearing crew member "served" on a ship with no telecomms, and
        // the alarm would be completely silent - the exact failure the fallback exists to prevent.
        var channelPrototype = _proto.Index(channelId);
        var relayAvailable = channelPrototype.LongRange || HasActiveTelecomServer(Transform(source).MapID, channelId);

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } body || Transform(body).GridUid != grid)
                continue;

            if (CanHearRadio(body, source, channelPrototype, relayAvailable))
                continue;

            _chat.DispatchServerMessage(session, text);
        }
    }

    /// <summary>
    ///     Announce, unless the same machine already announced something recently.
    /// </summary>
    /// <param name="nextAlert">
    ///     The machine's own cooldown clock - one per reactor, exactly like the DM <c>can_alert</c>
    ///     var, so two reactors on one ship complain independently.
    /// </param>
    /// <param name="urgent">
    ///     Bypass the cooldown. The DM original used this for "the core is overheating" and "the
    ///     rods have failed" so those can never be swallowed by an ongoing rod-integrity report.
    /// </param>
    /// <returns>True when the message was actually sent.</returns>
    public bool TryAnnounce(
        EntityUid source,
        string message,
        ref TimeSpan nextAlert,
        bool urgent = false,
        ProtoId<RadioChannelPrototype>? channel = null)
    {
        var now = _timing.CurTime;

        if (!urgent && now < nextAlert)
            return false;

        nextAlert = now + DefaultAlertCooldown;
        Announce(source, message, channel);
        return true;
    }

    /// <summary>
    ///     Broadcast an engine alarm on the Shortband, and nowhere else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Deliberately narrower than <see cref="Announce"/>: no direct chat fallback, because that
    ///     fallback is a server-side line of text that reads as a station-wide announcement - the
    ///     exact thing the engines are being taken off. The message goes out as ordinary radio
    ///     traffic, so it reaches whoever is actually listening to the Shortband: the ship's own
    ///     intercoms and shortband headsets, plus any vessel close enough to pick it up.
    ///     </para>
    ///     <para>
    ///     The machines carry <c>TelecomExempt</c>, so this does not depend on a telecomms server
    ///     being powered - an engine room with no relay still gets its alarm out.
    ///     </para>
    /// </remarks>
    public void EngineAnnounce(EntityUid source, string message)
    {
        var text = Loc.GetString("ship-event-announcement", ("ship", GetShipName(source)), ("message", message));
        _radio.SendRadioMessage(source, text, EngineChannel, source);
    }

    /// <summary>
    ///     <see cref="EngineAnnounce"/> with the per-machine cooldown gate.
    /// </summary>
    /// <returns>True when the message was actually sent.</returns>
    public bool TryEngineAnnounce(EntityUid source, string message, ref TimeSpan nextAlert, bool urgent = false)
    {
        var now = _timing.CurTime;

        if (!urgent && now < nextAlert)
            return false;

        nextAlert = now + DefaultAlertCooldown;
        EngineAnnounce(source, message);
        return true;
    }

    /// <summary>
    ///     Would this body receive a radio message on <paramref name="channel"/> sent from
    ///     <paramref name="source"/>? Mirrors the delivery test in <see cref="RadioSystem"/> closely
    ///     enough to decide whether the direct chat fallback is needed.
    /// </summary>
    /// <remarks>
    ///     A normal headset puts <see cref="ActiveRadioComponent"/> on the <em>item</em>, not on the
    ///     wearer (see <c>HeadsetSystem.UpdateRadioChannels</c>); only an implanted radio puts it on
    ///     the body. Checking the body alone would classify every ordinary crew member as deaf and
    ///     send them the fallback on top of the radio line, i.e. the duplicate this helper exists to
    ///     avoid.
    /// </remarks>
    private bool CanHearRadio(
        EntityUid body,
        EntityUid source,
        RadioChannelPrototype channel,
        bool relayAvailable)
    {
        if (!TryGetActiveRadio(body, out var radio))
            return false;

        if (!radio.ReceiveAllChannels && !radio.Channels.Contains(channel.ID))
            return false;

        var sourceXform = Transform(source);
        var bodyXform = Transform(body);

        if (!channel.LongRange && !radio.GlobalReceive && bodyXform.MapID != sourceXform.MapID)
            return false;

        if (channel.MaxRange is { } maxRange && maxRange > 0 &&
            (_transform.GetWorldPosition(bodyXform) - _transform.GetWorldPosition(sourceXform)).Length() > maxRange)
        {
            return false;
        }

        // Mirrors RadioSystem's `needServer` gate.
        if (!channel.LongRange &&
            !HasComp<TelecomExemptComponent>(source) &&
            !relayAvailable)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     The body's own radio, or the one on the headset it is wearing. Returns false when the
    ///     body has no working receiver at all.
    /// </summary>
    private bool TryGetActiveRadio(EntityUid body, [NotNullWhen(true)] out ActiveRadioComponent? radio)
    {
        if (TryComp(body, out radio))
            return true;

        if (TryComp<WearingHeadsetComponent>(body, out var wearing) &&
            TryComp(wearing.Headset, out radio))
        {
            return true;
        }

        radio = null;
        return false;
    }

    /// <summary>
    ///     Mirrors <c>RadioSystem.HasActiveServer</c>, which is private: a short-range channel only
    ///     leaves the radio when a powered telecomms server on the same map carries its encryption
    ///     key. Kept in sync with the engine implementation deliberately.
    /// </summary>
    private bool HasActiveTelecomServer(MapId mapId, ProtoId<RadioChannelPrototype> channelId)
    {
        var servers = EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent>();

        while (servers.MoveNext(out var serverUid, out _, out var keys, out var power))
        {
            if (!power.Powered)
                continue;

            if (Transform(serverUid).MapID != mapId)
                continue;

            if (!keys.Channels.Contains(channelId.Id))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>
    ///     Shake the ship. The client only kicks the camera of players whose grid matches, so
    ///     this is grid-scoped for free.
    /// </summary>
    /// <remarks>
    ///     Do not use this for machinery: a reactor that hisses once every two seconds must not
    ///     rattle a ship the size of a town. <see cref="ShakeLocal"/> is the machine-scoped version.
    /// </remarks>
    public void Shake(EntityUid source)
    {
        if (!TryGetGrid(source, out var grid) || !HasComp<GravityComponent>(grid))
            return;

        _gravity.StartGridShake(grid);
    }

    /// <summary>
    ///     Jolts the camera of the players standing near <paramref name="source"/>, and nobody else.
    /// </summary>
    /// <param name="source">Entity at the centre of the effect.</param>
    /// <param name="radius">How far the jolt is felt, in tiles.</param>
    /// <param name="strength">Multiplier on the kick magnitude (1 is a firm bump).</param>
    /// <remarks>
    ///     The DM originals shook mobs with <c>shake_with_inertia()</c> in <c>orange()</c> of the
    ///     machine - a local effect. SS14's <see cref="SharedGravitySystem.StartGridShake"/> shakes
    ///     an entire grid, which on a ship-based fork means the whole vessel, so the reactors use
    ///     this instead: three short camera kicks, alternating direction, at the source only.
    /// </remarks>
    public void ShakeLocal(EntityUid source, float radius = 6f, float strength = 1f)
    {
        if (TerminatingOrDeleted(source))
            return;

        var xform = Transform(source);
        var mapId = xform.MapID;
        var origin = _transform.GetWorldPosition(xform);
        var radiusSquared = radius * radius;

        var targets = new List<EntityUid>();

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player || TerminatingOrDeleted(player))
                continue;

            var playerXform = Transform(player);

            if (playerXform.MapID != mapId)
                continue;

            if ((_transform.GetWorldPosition(playerXform) - origin).LengthSquared() > radiusSquared)
                continue;

            targets.Add(player);
        }

        if (targets.Count == 0)
            return;

        // Three kicks a tenth of a second apart read as a shake rather than a single nudge.
        for (var i = 0; i < 3; i++)
        {
            var delay = TimeSpan.FromSeconds(i * 0.1f);

            Timer.Spawn(delay, () =>
            {
                foreach (var player in targets)
                {
                    if (TerminatingOrDeleted(player))
                        continue;

                    var direction = _random.NextAngle().ToVec();
                    _recoil.KickCamera(player, direction * (0.35f * strength));
                }
            });
        }
    }

    /// <summary>
    ///     Fry or flicker every light on the ship.
    /// </summary>
    /// <param name="destroyChance">Chance per fixture that the bulb is destroyed outright.</param>
    /// <param name="flickerChance">Chance per surviving fixture that it starts flickering.</param>
    /// <param name="flickerTime">
    ///     How long the survivors strobe for. They are switched back to steady afterwards: the
    ///     earlier version only ever turned blinking <em>on</em>, so a reactor failure left the whole
    ///     ship strobing for the rest of the round.
    /// </param>
    public void BlowLights(
        EntityUid source,
        float destroyChance,
        float flickerChance,
        TimeSpan? flickerTime = null)
    {
        if (!TryGetGrid(source, out var grid))
            return;

        var duration = flickerTime ?? DefaultFlickerTime;

        var lights = EntityQueryEnumerator<PoweredLightComponent>();
        while (lights.MoveNext(out var lightUid, out var light))
        {
            var xform = Transform(lightUid);
            if (xform.GridUid != grid)
                continue;

            if (_random.Prob(destroyChance))
            {
                _poweredLight.TryDestroyBulb(lightUid, light);
                continue;
            }

            if (!_random.Prob(flickerChance))
                continue;

            _poweredLight.ToggleBlinkingLight(lightUid, light, true);

            // Jitter the duration so a blown compartment does not resynchronise into one flickering
            // mass, then calm every fixture back down.
            var jitter = TimeSpan.FromSeconds(_random.NextFloat(0f, 5f));
            lightUid.SpawnTimer(duration + jitter, () =>
            {
                if (!Deleted(lightUid) && TryComp<PoweredLightComponent>(lightUid, out var stillLit))
                    _poweredLight.ToggleBlinkingLight(lightUid, stillLit, false);
            });
        }
    }

    /// <summary>
    ///     Non-destructive shove. Fling loose items and mobs away from the epicentre, with linear
    ///     falloff, and knock people over — but deal no damage at all.
    /// </summary>
    /// <remarks>
    ///     This is what makes a reactor failure feel like a hit rather than just a hole: the hull
    ///     survives, the crew does not get to stay standing, and everything not bolted down
    ///     becomes a projectile. Deliberately separate from the explosion so the destructive core
    ///     can stay small while the ship still feels the whole thing.
    /// </remarks>
    /// <param name="speed">
    ///     Knockback speed at the epicentre, in tiles per second. Scaled by each body's mass so the
    ///     result is a consistent speed rather than a consistent impulse.
    /// </param>
    public void Shockwave(
        EntityUid source,
        MapCoordinates epicentre,
        float radius,
        float speed,
        TimeSpan knockdown)
    {
        if (!TryGetGrid(source, out var grid))
            return;

        var bodies = _lookup.GetEntitiesInRange(epicentre, radius);

        foreach (var target in bodies)
        {
            if (target == source || Deleted(target))
                continue;

            // Only shove things that are actually on this ship.
            if (Transform(target).GridUid != grid)
                continue;

            if (!TryComp<PhysicsComponent>(target, out var physics) || physics.BodyType != BodyType.Dynamic)
                continue;

            var delta = _transform.GetWorldPosition(target) - epicentre.Position;
            var distance = delta.Length();

            if (distance >= radius)
                continue;

            var strength = 1f - distance / radius;
            var direction = distance > 0.01f ? delta / distance : _random.NextAngle().ToVec();

            _physics.ApplyLinearImpulse(target, direction * (physics.Mass * speed * strength), body: physics);

            if (knockdown <= TimeSpan.Zero || !TryComp<CrawlerComponent>(target, out var crawler))
                continue;

            var time = TimeSpan.FromSeconds(knockdown.TotalSeconds * strength);
            if (time > TimeSpan.Zero)
                _stun.TryKnockdown((target, crawler), time, force: true);
        }
    }

    /// <summary>
    ///     Pull everything within <paramref name="radius"/> toward the source, and knock standing
    ///     mobs down. This is NSV13's <c>grav_pull()</c> - an unstable reactor's warning shot.
    /// </summary>
    /// <param name="strength">Impulse applied at the epicentre, scaled down linearly with distance.</param>
    public void GravityPull(EntityUid source, float radius, float strength, TimeSpan knockdown)
    {
        if (!TryGetGrid(source, out var grid))
            return;

        var origin = _transform.GetWorldPosition(source);
        var bodies = _lookup.GetEntitiesInRange(new MapCoordinates(origin, Transform(source).MapID), radius);

        foreach (var target in bodies)
        {
            if (target == source || Deleted(target))
                continue;

            if (Transform(target).GridUid != grid)
                continue;

            var delta = origin - _transform.GetWorldPosition(target);
            var distance = delta.Length();

            if (distance >= radius)
                continue;

            var direction = distance > 0.01f ? delta / distance : _random.NextAngle().ToVec();
            var falloff = 1f - distance / radius;

            if (TryComp<PhysicsComponent>(target, out var physics) && physics.BodyType == BodyType.Dynamic)
            {
                _physics.ApplyLinearImpulse(target, direction * (physics.Mass * strength * falloff), body: physics);
            }

            if (knockdown <= TimeSpan.Zero || !TryComp<CrawlerComponent>(target, out var crawler))
                continue;

            var time = TimeSpan.FromSeconds(knockdown.TotalSeconds * falloff);

            if (time > TimeSpan.Zero)
                _stun.TryKnockdown((target, crawler), time, force: true);
        }
    }

    /// <summary>
    ///     Every non-space tile of the ship the entity is on. Empty by default so the slag storm
    ///     lands in corridors rather than sailing off into the void.
    /// </summary>
    public List<TileRef> GetShipTiles(EntityUid source)
    {
        var tiles = new List<TileRef>();

        if (!TryGetGrid(source, out var grid) || !TryComp<MapGridComponent>(grid, out var gridComp))
            return tiles;

        foreach (var tile in _map.GetAllTiles(grid, gridComp))
            tiles.Add(tile);

        return tiles;
    }

    /// <summary>Coordinates at the centre of a grid tile.</summary>
    public EntityCoordinates TileCentre(TileRef tile)
    {
        return _map.ToCenterCoordinates(tile);
    }

    /// <summary>
    ///     Throw chunks of corium from <paramref name="source"/> to random floor tiles on its own
    ///     ship. Replaces NSV13's map-wide fallout weather and its `nuclear_waste_spawner`
    ///     landmarks, both of which assume a single station-sized map.
    /// </summary>
    public void SpawnSlagStorm(EntityUid source, int count, EntProtoId prototype, float speed)
    {
        var tiles = GetShipTiles(source);

        if (tiles.Count == 0)
            return;

        var origin = _transform.GetMapCoordinates(source);

        for (var i = 0; i < count; i++)
        {
            var target = _transform.ToMapCoordinates(TileCentre(_random.Pick(tiles)));
            var offset = target.Position - origin.Position;

            // A degenerate offset means the only floors available are the source's own tile - a
            // very small ship, or a freshly built compartment. Throw somewhere random rather than
            // dropping the chunk inside the machine.
            if (offset.LengthSquared() < 0.01f)
                offset = _random.NextAngle().ToVec() * 2f;

            // Spawn just outside the machine's own collision, otherwise the chunks start the throw
            // already interpenetrating it and get shoved out at a random angle.
            var spawnPos = origin.Position + offset.Normalized() * 0.75f;
            var slag = Spawn(prototype, new MapCoordinates(spawnPos, origin.MapId));

            _throwing.TryThrow(slag,
                offset,
                speed,
                compensateFriction: true,
                playSound: false);
        }
    }
}
