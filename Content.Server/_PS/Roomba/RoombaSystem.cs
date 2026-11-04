using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Decals;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._PS.Roomba;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._PS.Roomba;

/// <summary>
/// Palmtree: runs roombas (<see cref="RoombaComponent"/>). Unlike the cleanbot it does not plan
/// routes: it drives in a straight line, turns when it bumps into something or runs out of floor,
/// and never targets a tile that would take it off the grid into space.
/// </summary>
/// <remarks>
/// Movement goes through <see cref="NPCSteeringSystem"/>'s direct move, which handles acceleration,
/// collision avoidance and facing. Cleaning happens underfoot: every <see cref="RoombaComponent.ScrubDistance"/>
/// tiles the bot scrubs cleanable decals around itself (the same set a space cleaner removes) and
/// mops puddles on its tile with the cleanbot's own tank. Pitfalls: decal grids only exist on grids,
/// and all of this is server-only.
/// </remarks>
public sealed class RoombaSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedAbsorbentSystem _absorbent = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private static readonly string[] BumpLines =
    {
        "roomba-say-bump-ouch",
        "roomba-say-bump-owie",
        "roomba-say-bump-woops",
        "roomba-say-bump-fuck",
        "roomba-say-bump-circuit-board",
    };

    private static readonly string[] IdleLines =
    {
        "roomba-say-idle-1",
        "roomba-say-idle-2",
        "roomba-say-idle-3",
        "roomba-say-idle-4",
        "roomba-say-idle-5",
        "roomba-say-idle-6",
        "roomba-say-idle-7",
        "roomba-say-idle-8",
        "roomba-say-idle-9",
        "roomba-say-idle-10",
        "roomba-say-idle-11",
        "roomba-say-idle-12",
        "roomba-say-idle-13",
    };

    private static readonly string[] NudeLines =
    {
        "roomba-say-nude-1",
        "roomba-say-nude-2",
        "roomba-say-nude-3",
        "roomba-say-nude-4",
        "roomba-say-nude-5",
    };

    private readonly HashSet<Entity<PuddleComponent>> _puddles = new();
    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _humanoids = new();
    private readonly HashSet<EntityUid> _obstacles = new();
    private readonly HashSet<EntityUid> _surfaces = new();
    private readonly HashSet<EntityUid> _touching = new();
    private readonly HashSet<EntityUid> _trash = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoombaComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RoombaComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoombaComponent, InteractHandEvent>(OnInteractHand,
            before: new[] { typeof(InteractionPopupSystem) });
        SubscribeLocalEvent<RoombaComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<RoombaComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<RoombaComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, RoombaComponent roomba, MapInitEvent args)
    {
        var now = _timing.CurTime;
        var xform = Transform(uid);

        // The engine detaches entities from a grid the moment they step onto an empty tile; a roomba
        // that overshoots the floor edge should instead keep the grid as its parent so the recovery
        // below can put it back. Entity lookups may miss it for the tick or two this takes.
        xform.GridTraversal = false;

        roomba.NextIdleSpeech = now + TimeSpan.FromSeconds(
            _random.NextFloat(roomba.IdleSpeechMinSeconds, roomba.IdleSpeechMaxSeconds));
        roomba.NextNudeCheck = now + TimeSpan.FromSeconds(roomba.NudeCheckInterval);
        roomba.LastScrubPosition = xform.LocalPosition;
        roomba.LastSafeCoordinates = xform.Coordinates;

        // Roombas are shipped switched off. Sleep the NPC right away when we can (the HTN may wake
        // it again during its own MapInit, but Update also refuses to drive a disabled roomba).
        _appearance.SetData(uid, RoombaVisuals.Enabled, roomba.Enabled);
        if (!roomba.Enabled)
            _npc.SleepNPC(uid);
    }

    private void OnShutdown(EntityUid uid, RoombaComponent roomba, ComponentShutdown args)
    {
        if (HasComp<NPCSteeringComponent>(uid))
            _steering.Unregister(uid);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RoombaComponent, ActiveNPCComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var roomba, out _, out var xform))
        {
            // A disabled roomba is normally asleep, but the HTN can wake it during its own MapInit
            // or if something else pokes the NPC. Never drive, clean or talk while switched off.
            if (!roomba.Enabled)
                continue;

            UpdateRoomba(uid, roomba, xform, now);
        }
    }

    private void UpdateRoomba(EntityUid uid, RoombaComponent roomba, TransformComponent xform, TimeSpan now)
    {
        // Off the grid entirely (the engine's grid traversal detaches entities that step onto an
        // empty tile): rubber-band back to the last spot we know is solid floor.
        if (xform.GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
        {
            TryRecover(uid, roomba, xform, now);
            return;
        }

        var pos = xform.LocalPosition;

        if (!IsWalkable(grid, gridComp, pos))
        {
            TryRecover(uid, roomba, xform, now);
            return;
        }

        roomba.LastSafeCoordinates = xform.Coordinates;

        var steering = CompOrNull<NPCSteeringComponent>(uid);

        if (roomba.LegActive)
        {
            if (steering == null)
            {
                // Someone else pulled the steering out from under us.
                roomba.LegActive = false;
            }
            else if (steering.Status == SteeringStatus.NoPath)
            {
                // The engine's anti-stuck gave up on this leg: that counts as a bump.
                StopDriving(uid, roomba);
                Bump(uid, roomba, pos, now);
            }
            else if (steering.Status == SteeringStatus.InRange ||
                     (pos - roomba.Target.Position).Length() <= 0.3f)
            {
                // Arrived; pick a new leg in a moment. Steering stays registered for a smooth turn.
                roomba.LegActive = false;
            }
            else if ((pos - roomba.ProgressPosition).LengthSquared() >=
                     roomba.BumpDistance * roomba.BumpDistance)
            {
                roomba.ProgressPosition = pos;
                roomba.ProgressTime = now;
            }
            else if (now - roomba.ProgressTime >= TimeSpan.FromSeconds(roomba.BumpTime))
            {
                // Pushing against something and going nowhere: bump.
                StopDriving(uid, roomba);
                Bump(uid, roomba, pos, now);
            }
        }

        if (!roomba.LegActive && now >= roomba.NextLegRetry)
            StartLeg(uid, roomba, grid, gridComp, pos, now);

        TryComp(grid, out DecalGridComponent? decalGrid);
        Scrub(uid, roomba, grid, gridComp, decalGrid, pos);
        Speak(uid, roomba, now);
    }

    /// <summary>
    /// Puts the roomba back on the tile it last reported as solid floor: it can overshoot the edge of
    /// the floor while carrying momentum, and must never be left floating over space.
    /// </summary>
    private void TryRecover(EntityUid uid, RoombaComponent roomba, TransformComponent xform, TimeSpan now)
    {
        StopDriving(uid, roomba);

        var safe = roomba.LastSafeCoordinates;
        if (!safe.IsValid(EntityManager) || !HasComp<MapGridComponent>(safe.EntityId))
            return;

        if (_transform.GetMapId(xform.Coordinates) != _transform.GetMapId(safe))
            return;

        _transform.SetCoordinates(uid, safe);
        roomba.LegActive = false;
        roomba.LastScrubPosition = safe.Position;
        roomba.ProgressPosition = safe.Position;
        roomba.ProgressTime = now;

        // A short pause before driving off again so the recovery is not a teleport-stutter loop.
        roomba.NextLegRetry = now + TimeSpan.FromSeconds(0.5);
    }

    /// <summary>
    /// Turns the roomba away from whatever it just hit and maybe complains about it.
    /// </summary>
    private void Bump(EntityUid uid, RoombaComponent roomba, Vector2 pos, TimeSpan now)
    {
        var turn = _random.NextFloat(roomba.BumpTurnMin, roomba.BumpTurnMax);
        if (_random.Next(2) == 1)
            turn = -turn;

        roomba.Heading = RotateHeading(roomba.Heading, turn);
        roomba.ProgressPosition = pos;
        roomba.ProgressTime = now;
        roomba.NextLegRetry = now;

        if (now < roomba.NextBumpSpeech || !_random.Prob(roomba.BumpSpeechChance))
            return;

        roomba.NextBumpSpeech = now + TimeSpan.FromSeconds(roomba.BumpSpeechCooldown);
        Say(uid, roomba, _random.Pick(BumpLines));
    }

    /// <summary>
    /// Picks a clear straight stretch of floor to drive down. Two passes: cheap tile checks first
    /// (this is what keeps the roomba on its grid), then a broadphase pass for walls, machines and
    /// mobs so it does not grind into furniture. If everything ahead is blocked it drives at the
    /// least bad option anyway - that is the bump.
    /// </summary>
    private void StartLeg(EntityUid uid, RoombaComponent roomba, EntityUid grid, MapGridComponent gridComp,
        Vector2 pos, TimeSpan now)
    {
        if (roomba.Heading == Vector2.Zero)
            roomba.Heading = _random.NextAngle().ToVec();

        var distance = _random.NextFloat(roomba.MinLegDistance, roomba.MaxLegDistance);

        // Pass 1: floor. Directions that would cross space are rejected.
        var candidates = new List<(Vector2 Direction, Vector2 Target)>();
        var fallbackDirection = Vector2.Zero;
        var fallbackTarget = Vector2.Zero;
        var hasFallback = false;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            Vector2 candidateDirection;
            if (attempt == 0)
            {
                // Mostly keep going the way we were, with a little drift.
                candidateDirection = RotateHeading(roomba.Heading,
                    _random.NextFloat(-roomba.LegJitter, roomba.LegJitter));
            }
            else
            {
                candidateDirection = _random.NextAngle().ToVec();
            }

            // Later attempts shorten up so a roomba in a corner can still find a way out, and relax
            // the momentum margin a bit so a bot next to an edge can still creep along.
            var length = attempt < 6 ? distance : MathF.Max(0.5f, roomba.MinLegDistance * 0.6f);
            var margin = attempt < 6 ? roomba.EdgeMargin : roomba.EdgeMargin * 0.5f;
            var candidate = pos + candidateDirection * length;

            if (!IsLineClear(grid, gridComp, pos, candidate, roomba.LineCheckStep, margin))
                continue;

            if (!hasFallback)
            {
                hasFallback = true;
                fallbackDirection = candidateDirection;
                fallbackTarget = candidate;
            }

            candidates.Add((candidateDirection, candidate));
        }

        // Pass 2: whatever is standing on that floor.
        var found = false;
        var direction = fallbackDirection;
        var target = fallbackTarget;

        foreach (var (candidateDirection, candidate) in candidates)
        {
            if (!IsLineClearOfObstacles(uid, roomba, grid, pos, candidate))
                continue;

            found = true;
            direction = candidateDirection;
            target = candidate;
            break;
        }

        if (!found && hasFallback)
        {
            // Wedged: take the least bad option and let the bump handling turn us away when we hit it.
            found = true;
        }

        if (!found)
        {
            // Nowhere to go at all right now; don't spin the picker every tick.
            roomba.NextLegRetry = now + TimeSpan.FromSeconds(0.25);
            roomba.LegActive = false;
            return;
        }

        roomba.Heading = direction;
        roomba.Target = new EntityCoordinates(grid, target);

        // Kill momentum on a hard turn so the roomba cannot coast off the floor edge; gentle turns
        // keep their speed and flow into the new leg.
        if (TryComp(uid, out PhysicsComponent? body))
        {
            var velocity = body.LinearVelocity;
            if (velocity.LengthSquared() > 0.01f && Vector2.Dot(velocity.Normalized(), direction) < 0.5f)
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
        }

        var steering = _steering.Register(uid, roomba.Target);
        steering.DirectMove = true;
        steering.Range = 0.3f;
        steering.InRangeMaxSpeed = null;

        roomba.LegActive = true;
        roomba.ProgressPosition = pos;
        roomba.ProgressTime = now;
    }

    /// <summary>
    /// Checks a leg for things the roomba would bump into: walls, machines and other mobs. Furniture
    /// it fits under (tables, chairs) does not collide with it, so it is ignored and the roomba rolls
    /// right under. Obstacles the roomba is already overlapping are skipped so it can squeeze out of
    /// a corner (mobs are only skipped when it is practically on top of them).
    /// </summary>
    private bool IsLineClearOfObstacles(EntityUid uid, RoombaComponent roomba, EntityUid grid, Vector2 from,
        Vector2 to)
    {
        if (!TryComp(uid, out FixturesComponent? ourFixtures) || !TryComp(uid, out PhysicsComponent? ourBody))
            return true;

        var delta = to - from;
        var length = delta.Length();

        if (length < 0.05f)
            return false;

        var direction = delta / length;
        var radius = roomba.ObstacleRadius;
        var half = new Vector2(radius);

        // Things we are already touching do not count as obstacles for this leg.
        _touching.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, new Box2(from - half, from + half), _touching,
            LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Approximate);

        for (var travelled = radius; travelled <= length; travelled += roomba.LineCheckStep)
        {
            var point = from + direction * travelled;
            _obstacles.Clear();
            _lookup.GetLocalEntitiesIntersecting(grid, new Box2(point - half, point + half), _obstacles,
                LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Approximate);

            foreach (var other in _obstacles)
            {
                if (other == uid)
                    continue;

                if (TryComp(other, out MobStateComponent? mobState))
                {
                    if (mobState.CurrentState != MobState.Alive)
                        continue;

                    // Mobs do not physically collide with the roomba, but driving through people is
                    // rude. Skip ones we are already under so it can get out from under their feet.
                    if (_touching.Contains(other))
                        continue;

                    if (TryGetLocalPosition(other, grid, out var mobPos) &&
                        (mobPos - from).LengthSquared() <= (radius + 0.25f) * (radius + 0.25f))
                    {
                        continue;
                    }

                    return false;
                }

                if (_touching.Contains(other))
                    continue;

                if (!TryComp(other, out PhysicsComponent? otherBody) ||
                    !_physics.IsCurrentlyHardCollidable((uid, ourFixtures, ourBody), (other, null, otherBody)))
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private bool TryGetLocalPosition(EntityUid uid, EntityUid grid, out Vector2 position)
    {
        position = Vector2.Zero;

        if (!TryComp(uid, out TransformComponent? xform) || xform.ParentUid != grid)
            return false;

        position = xform.LocalPosition;
        return true;
    }

    private void StopDriving(EntityUid uid, RoombaComponent roomba)
    {
        roomba.LegActive = false;

        if (HasComp<NPCSteeringComponent>(uid))
            _steering.Unregister(uid);
    }

    /// <summary>
    /// Scrubs cleanable decals around the roomba and mops a puddle on its tile. Runs once every
    /// <see cref="RoombaComponent.ScrubDistance"/> tiles of travel, so tiles are not skipped.
    /// </summary>
    private void Scrub(EntityUid uid, RoombaComponent roomba, EntityUid grid, MapGridComponent gridComp,
        DecalGridComponent? decalGrid, Vector2 pos)
    {
        if ((pos - roomba.LastScrubPosition).LengthSquared() < roomba.ScrubDistance * roomba.ScrubDistance)
            return;

        roomba.LastScrubPosition = pos;

        CollectTrash(uid, roomba, grid, pos);

        // Puddles first (same tank and use delay as the cleanbot).
        _puddles.Clear();
        var tile = _map.TileIndicesFor(grid, gridComp, new EntityCoordinates(grid, pos));
        _lookup.GetLocalEntitiesIntersecting(grid, tile, _puddles, flags: LookupFlags.Uncontained);

        if (_puddles.Count > 0 && TryComp(uid, out AbsorbentComponent? absorbent))
        {
            foreach (var puddle in _puddles)
            {
                _absorbent.Mop((uid, absorbent), uid, puddle.Owner);
                return;
            }
        }

        if (decalGrid == null)
            return;

        var half = roomba.ScrubRadius;
        var bounds = new Box2(pos - new Vector2(half, half), pos + new Vector2(half, half));
        var decals = _decals.GetDecalsIntersecting(grid, bounds, decalGrid);

        if (decals.Count == 0)
            return;

        var cleaned = 0;
        foreach (var (index, decal) in decals)
        {
            if (!decal.Cleanable)
                continue;

            _decals.RemoveDecal(grid, index, decalGrid);
            cleaned++;
        }

        if (cleaned == 0)
            return;

        ConsumeWater(uid, roomba, cleaned);

        // A very dirty floor can be scrubbed every tick or two; keep the vacuum sound from turning
        // into a machine gun.
        if (_timing.CurTime < roomba.NextScrubSound)
            return;

        roomba.NextScrubSound = _timing.CurTime + TimeSpan.FromSeconds(roomba.ScrubSoundCooldown);
        _audio.PlayPvs(roomba.ScrubSound, uid);
    }

    /// <summary>
    /// Vacuums up loose trash around the roomba, using the same whitelist as a trash bag's area insert.
    /// Items resting on a table (or another placeable surface) are deliberately left alone: they are
    /// not on the floor, even though they share the tile's coordinates.
    /// </summary>
    private void CollectTrash(EntityUid uid, RoombaComponent roomba, EntityUid grid, Vector2 pos)
    {
        if (!roomba.CollectTrash || !TryGetTrashBagStorage(uid, out var bag))
            return;

        var half = new Vector2(roomba.TrashPickupRadius);
        _trash.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, new Box2(pos - half, pos + half), _trash,
            LookupFlags.Uncontained);

        var picked = 0;
        foreach (var other in _trash)
        {
            if (other == uid || _container.IsEntityInContainer(other))
                continue;

            if (IsOnPlaceableSurface(other))
                continue;

            if (!_storage.CanInsert(bag.Owner, other, out _, bag.Comp))
                continue;

            if (!_storage.Insert(bag.Owner, other, out _, storageComp: bag.Comp, playSound: false))
                continue;

            picked++;
            if (picked >= roomba.TrashPickupLimit)
                break;
        }

        if (picked > 0)
            _audio.PlayPvs(roomba.TrashPickupSound, uid);
    }

    /// <summary>
    /// Returns true when the entity is sitting on something like a table, bed or crate instead of
    /// the floor. SS14 has no z-levels, so "on a table" is represented by the item overlapping a
    /// <see cref="PlaceableSurfaceComponent"/>.
    /// </summary>
    private bool IsOnPlaceableSurface(EntityUid item)
    {
        _surfaces.Clear();
        _lookup.GetEntitiesIntersecting(item, _surfaces, LookupFlags.Static | LookupFlags.Dynamic);
        _surfaces.Remove(item);

        foreach (var other in _surfaces)
        {
            if (HasComp<PlaceableSurfaceComponent>(other))
                return true;
        }

        return false;
    }

    private bool TryGetTrashBagStorage(EntityUid uid, out Entity<StorageComponent> bag)
    {
        bag = default;

        if (!_container.TryGetContainer(uid, RoombaComponent.TrashBagContainerId, out var container))
            return false;

        foreach (var entity in container.ContainedEntities)
        {
            if (TryComp(entity, out StorageComponent? storage))
            {
                bag = (entity, storage);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Switches the roomba on or off. A powered-down roomba is put to sleep so the NPC system does
    /// not keep planning for it; switching it back on wakes it and resets its timers.
    /// </summary>
    private void SetEnabled(EntityUid uid, RoombaComponent roomba, bool enabled, EntityUid? user = null,
        bool playSound = true)
    {
        if (roomba.Enabled == enabled)
            return;

        roomba.Enabled = enabled;

        if (enabled)
        {
            var now = _timing.CurTime;
            roomba.NextLegRetry = now;
            roomba.NextBumpSpeech = now;
            roomba.NextNudeSpeech = now;
            roomba.NextNudeCheck = now + TimeSpan.FromSeconds(roomba.NudeCheckInterval);
            roomba.NextIdleSpeech = now + TimeSpan.FromSeconds(
                _random.NextFloat(roomba.IdleSpeechMinSeconds, roomba.IdleSpeechMaxSeconds));
            roomba.LastScrubPosition = Transform(uid).LocalPosition;
            _npc.WakeNPC(uid);
        }
        else
        {
            StopDriving(uid, roomba);

            if (TryComp(uid, out PhysicsComponent? body))
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);

            _npc.SleepNPC(uid);
        }

        _appearance.SetData(uid, RoombaVisuals.Enabled, enabled);

        if (user is { } player)
            _popup.PopupEntity(Loc.GetString(enabled ? "roomba-power-on" : "roomba-power-off"), uid, player);

        if (playSound)
            _audio.PlayPvs(roomba.PowerSound, uid);
    }

    private void TogglePower(EntityUid uid, RoombaComponent roomba, EntityUid user)
    {
        SetEnabled(uid, roomba, !roomba.Enabled, user);
    }

    private void OnInteractHand(EntityUid uid, RoombaComponent roomba, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        // Clicking a roomba switches it instead of petting it. Handling the event here also stops
        // the inherited InteractionPopup component from playing the cleanbot petting lines.
        args.Handled = true;
        TogglePower(uid, roomba, args.User);
    }

    private void OnActivateInWorld(EntityUid uid, RoombaComponent roomba, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TogglePower(uid, roomba, args.User);
    }

    private void OnGetVerbs(EntityUid uid, RoombaComponent roomba, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == uid)
            return;

        var user = args.User;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(roomba.Enabled ? "roomba-verb-power-off" : "roomba-verb-power-on"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/Spare/poweronoff.svg.192dpi.png")),
            Priority = 10,
            Impact = LogImpact.Low,
            Act = () => SetEnabled(uid, roomba, !roomba.Enabled, user),
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(roomba.CollectTrash ? "roomba-verb-trash-off" : "roomba-verb-trash-on"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/delete.svg.192dpi.png")),
            Priority = 5,
            Impact = LogImpact.Low,
            Act = () =>
            {
                roomba.CollectTrash = !roomba.CollectTrash;
                _popup.PopupEntity(Loc.GetString(roomba.CollectTrash
                    ? "roomba-trash-collection-enabled"
                    : "roomba-trash-collection-disabled"), uid, user);
            },
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(roomba.MuteSpeech ? "roomba-verb-unmute" : "roomba-verb-mute"),
            Icon = new SpriteSpecifier.Texture(new(roomba.MuteSpeech
                ? "/Textures/_DV/Interface/VerbIcons/bell.svg.png"
                : "/Textures/_DV/Interface/VerbIcons/bell_muted.png")),
            Priority = 4,
            Impact = LogImpact.Low,
            Act = () =>
            {
                roomba.MuteSpeech = !roomba.MuteSpeech;
                _popup.PopupEntity(Loc.GetString(roomba.MuteSpeech
                    ? "roomba-speech-muted"
                    : "roomba-speech-unmuted"), uid, user);
            },
        });

        if (!TryGetTrashBagStorage(uid, out var bag) || bag.Comp.Container.ContainedEntities.Count == 0)
            return;

        var hasFreeHand = _hands.TryGetEmptyHand(user, out _);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("roomba-verb-take-trash-bag"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/drop.svg.192dpi.png")),
            Priority = 3,
            Impact = LogImpact.Low,
            Disabled = !hasFreeHand,
            Message = hasFreeHand ? null : Loc.GetString("roomba-trash-bag-needs-hand"),
            Act = () => TakeTrashBag(uid, roomba, user),
        });
    }

    private void OnExamined(EntityUid uid, RoombaComponent roomba, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(roomba.Enabled ? "roomba-examine-on" : "roomba-examine-off"));

        if (!roomba.Enabled)
            return;

        args.PushMarkup(Loc.GetString(roomba.CollectTrash
            ? "roomba-examine-trash-on"
            : "roomba-examine-trash-off"));

        if (roomba.MuteSpeech)
            args.PushMarkup(Loc.GetString("roomba-examine-muted"));
    }

    /// <summary>
    /// Takes the full internal trash bag and puts a fresh empty one in its place. Only retrieval is
    /// allowed: the bag is not a normal storage interaction, so players cannot insert items into it.
    /// </summary>
    private void TakeTrashBag(EntityUid uid, RoombaComponent roomba, EntityUid user)
    {
        if (!TryGetTrashBagStorage(uid, out var bag) || bag.Comp.Container.ContainedEntities.Count == 0)
            return;

        if (!_hands.TryGetEmptyHand(user, out _))
        {
            _popup.PopupEntity(Loc.GetString("roomba-trash-bag-needs-hand"), uid, user);
            return;
        }

        if (!_container.TryGetContainer(uid, RoombaComponent.TrashBagContainerId, out var container))
            return;

        _container.Remove(bag.Owner, container, force: true);

        var newBag = Spawn("TrashBag", Transform(uid).Coordinates);
        if (!_container.Insert(newBag, container))
        {
            Log.Error($"Failed to insert a fresh trash bag into {ToPrettyString(uid)}.");
            _transform.AttachToGridOrMap(newBag);
        }

        if (!_hands.TryPickupAnyHand(user, bag.Owner))
        {
            _popup.PopupEntity(Loc.GetString("roomba-trash-bag-dropped"), uid, user);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("roomba-trash-bag-taken"), uid, user);
        }

        _audio.PlayPvs(roomba.TrashPickupSound, uid);
    }

    private void ConsumeWater(EntityUid uid, RoombaComponent roomba, int decals)
    {
        if (!_solutions.TryGetSolution(uid, "absorbed", out var solutionEnt, out var solution) ||
            solution.Volume <= FixedPoint2.Zero)
        {
            return;
        }

        _solutions.RemoveReagent(solutionEnt.Value, "Water", roomba.WaterPerDecal * decals);
    }

    /// <summary>
    /// Idle chatter, plus the occasional comment about characters who forgot their clothes.
    /// </summary>
    private void Speak(EntityUid uid, RoombaComponent roomba, TimeSpan now)
    {
        if (now >= roomba.NextIdleSpeech)
        {
            roomba.NextIdleSpeech = now + TimeSpan.FromSeconds(
                _random.NextFloat(roomba.IdleSpeechMinSeconds, roomba.IdleSpeechMaxSeconds));
            Say(uid, roomba, _random.Pick(IdleLines));
        }

        if (now < roomba.NextNudeCheck)
            return;

        roomba.NextNudeCheck = now + TimeSpan.FromSeconds(roomba.NudeCheckInterval);

        if (now < roomba.NextNudeSpeech || !CanSeeNudeCharacter(uid, roomba))
            return;

        if (!_random.Prob(roomba.NudeSpeechChance))
            return;

        roomba.NextNudeSpeech = now + TimeSpan.FromSeconds(roomba.NudeSpeechCooldown);
        Say(uid, roomba, _random.Pick(NudeLines));
    }

    /// <summary>
    /// Looks for a living character in view that is wearing neither a jumpsuit nor outer clothing.
    /// </summary>
    private bool CanSeeNudeCharacter(EntityUid uid, RoombaComponent roomba)
    {
        _humanoids.Clear();
        var xform = Transform(uid);
        _lookup.GetEntitiesInRange(xform.Coordinates, roomba.NudeCheckRange, _humanoids,
            LookupFlags.Dynamic | LookupFlags.Approximate);

        var found = false;
        foreach (var (other, _) in _humanoids)
        {
            if (other == uid)
                continue;

            if (!TryComp(other, out MobStateComponent? mobState) || mobState.CurrentState != MobState.Alive)
                continue;

            if (_inventory.TryGetSlotEntity(other, "jumpsuit", out _) ||
                _inventory.TryGetSlotEntity(other, "outerClothing", out _))
            {
                continue;
            }

            if (!_interaction.InRangeUnobstructed(uid, other, roomba.NudeCheckRange))
                continue;

            found = true;
            break;
        }

        _humanoids.Clear();
        return found;
    }

    private void Say(EntityUid uid, RoombaComponent roomba, string line)
    {
        if (roomba.MuteSpeech)
            return;

        // HideChat turns this into an overhead speech bubble only: like a vending machine, the
        // roomba is heard nearby but never enters chat history (and NPCs are not admin-logged).
        _chat.TrySendInGameICMessage(uid, Loc.GetString(line), InGameICChatType.Speak,
            hideChat: true, hideLog: true, checkRadioPrefix: false);
    }

    private bool IsLineClear(EntityUid grid, MapGridComponent gridComp, Vector2 from, Vector2 to, float step,
        float overshoot)
    {
        var delta = to - from;
        var length = delta.Length();

        if (length < 0.05f)
            return false;

        var direction = delta / length;
        var checkedLength = length + overshoot;

        for (var travelled = step; travelled < checkedLength; travelled += step)
        {
            if (!IsWalkable(grid, gridComp, from + direction * travelled))
                return false;
        }

        // The roomba carries momentum, so the target tile needs floor around it as well: a bot that
        // rolls a little past its goal must still land on solid floor.
        if (!IsWalkable(grid, gridComp, to))
            return false;

        if (overshoot <= 0f)
            return true;

        for (var i = 0; i < 8; i++)
        {
            var offset = Angle.FromDegrees(i * 45f).ToVec() * overshoot;
            if (!IsWalkable(grid, gridComp, to + offset))
                return false;
        }

        return true;
    }

    private bool IsWalkable(EntityUid grid, MapGridComponent gridComp, Vector2 localPosition)
    {
        if (!_map.TryGetTileRef(grid, gridComp, new EntityCoordinates(grid, localPosition), out var tile))
            return false;

        return !tile.Tile.IsEmpty && !_turf.IsSpace(tile);
    }

    private static Vector2 RotateHeading(Vector2 heading, float degrees)
    {
        if (heading == Vector2.Zero)
            return heading;

        return (heading.ToAngle() + Angle.FromDegrees(degrees)).ToVec();
    }
}
