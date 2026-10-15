using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Shared._PS.HilbertHotel;
using Content.Shared._PS.HilbertHotel.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._PS.HilbertHotel;

/// <summary>
/// Loads, tracks and unloads Hilbert's Hotel rooms.
/// </summary>
/// <remarks>
/// A room is one runtime-loaded map, keyed by a shareable numeric code. Rooms are
/// created on demand by <see cref="HilbertHotelTeleporterComponent"/> terminals and
/// kept loaded while occupied. Once empty a room is kept for a grace period and
/// then deleted, taking anything left inside with it. Rooms are deliberately never
/// paused: freezing a map also freezes any player who returns to their body inside
/// it (for example after admin ghosting).
/// </remarks>
public sealed class HilbertHotelSystem : EntitySystem
{
    /// <summary>
    /// How often room occupancy is checked.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long an empty room is kept before its map is deleted.
    /// </summary>
    private static readonly TimeSpan ReservedDuration = TimeSpan.FromMinutes(5);

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    private readonly Dictionary<int, HilbertHotelRoom> _rooms = new();

    /// <summary>
    /// When each terminal may next create a room (see <c>HilbertHotelTeleporterComponent.CreateCooldown</c>).
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextRoomCreation = new();

    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_rooms.Count == 0)
            return;

        var now = _timing.CurTime;
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;

        List<int>? toDelete = null;
        foreach (var (code, room) in _rooms)
        {
            if (!_map.MapExists(room.MapId))
            {
                (toDelete ??= new List<int>()).Add(code);
                continue;
            }

            if (HasLivingOccupants(room))
            {
                room.EmptySince = null;
                continue;
            }

            room.EmptySince ??= now;
            if (room.EmptySince.Value + ReservedDuration <= now)
                (toDelete ??= new List<int>()).Add(code);
        }

        if (toDelete == null)
            return;

        foreach (var code in toDelete)
            DeleteRoom(code);
    }

    #region Check-in / check-out

    /// <summary>
    /// Attempts to join the room with the given code, creating it from the chosen
    /// archetype when no room with that code exists yet.
    /// </summary>
    public bool TryCheckIn(EntityUid teleporter, EntityUid user, int code, string? templateId)
    {
        if (!TryComp<ActorComponent>(user, out var actor) ||
            !TryComp<HilbertHotelTeleporterComponent>(teleporter, out var teleporterComp))
        {
            return false;
        }

        var userId = actor.PlayerSession.UserId;

        if (!_transform.InRange(user, teleporter, 2f))
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-too-far"), user, user);
            return false;
        }

        if (_mobState.IsIncapacitated(user))
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-incapacitated"), user, user);
            return false;
        }

        if (code is < HilbertHotelConstants.MinRoomCode or > HilbertHotelConstants.MaxRoomCode)
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-invalid-code"), user, user);
            return false;
        }

        if (_rooms.TryGetValue(code, out var room))
        {
            if (!CanJoin(room, userId))
            {
                var error = room.Status == HilbertHotelRoomStatus.Locked
                    ? "hilbert-hotel-error-locked"
                    : "hilbert-hotel-error-guests-only";
                _popup.PopupEntity(Loc.GetString(error), user, user);
                return false;
            }

            JoinRoom(room, user, userId);
            return true;
        }

        // The limit is per terminal, not global: each terminal keeps its own pick of rooms, and a
        // low cap on one terminal must not throttle check-ins at another.
        if (CountRoomsFor(teleporter) >= teleporterComp.MaxRooms)
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-full"), user, user);
            return false;
        }

        // Rate-limit creation so delete/recreate cycles cannot spam map loads (0 disables it).
        var now = _timing.CurTime;
        if (teleporterComp.CreateCooldown > TimeSpan.Zero &&
            _nextRoomCreation.TryGetValue(teleporter, out var nextCreate) && now < nextCreate)
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-cooldown"), user, user);
            return false;
        }

        // A player may only own one room at a time; they must delete it first.
        if (TryGetOwnedRoom(userId, out var ownedRoom))
        {
            _popup.PopupEntity(
                Loc.GetString("hilbert-hotel-error-already-owns", ("code", ownedRoom.Code)),
                user,
                user);
            return false;
        }

        var template = ResolveTemplate(templateId);
        if (template == null)
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-no-templates"), user, user);
            return false;
        }

        if (teleporterComp.CreateCooldown > TimeSpan.Zero)
            _nextRoomCreation[teleporter] = now + teleporterComp.CreateCooldown;

        return TryCreateRoom(teleporter, teleporterComp, user, userId, code, template);
    }

    /// <summary>
    /// Teleports a player out of the room whose map they are currently standing on.
    /// </summary>
    public bool TryLeaveRoom(EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor) ||
            !TryGetRoomByMap(Transform(user).MapID, out var room))
        {
            return false;
        }

        var destination = room.DefaultReturn;
        if (room.EntryPoints.TryGetValue(actor.PlayerSession.UserId, out var entry))
            destination = entry;

        if (!_map.MapExists(destination.MapId))
            destination = new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap);

        _transform.SetMapCoordinates(user, destination);
        _popup.PopupEntity(Loc.GetString("hilbert-hotel-popup-left"), user, user);
        NotifyRoomChanged(room);
        return true;
    }

    private bool TryCreateRoom(
        EntityUid teleporter,
        HilbertHotelTeleporterComponent teleporterComp,
        EntityUid user,
        NetUserId userId,
        int code,
        HilbertHotelRoomPrototype template)
    {
        if (!_mapLoader.TryLoadMap(template.MapPath, out var map, out var grids))
        {
            Log.Error($"Hilbert's Hotel failed to load room map '{template.MapPath}' for room {code}.");
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-load-failed"), user, user);
            return false;
        }

        var mapUid = map.Value.Owner;
        var mapId = map.Value.Comp.MapId;

        // The loader leaves runtime-loaded maps paused; run map init and wake it up.
        if (!_map.IsInitialized(mapId))
            _map.InitializeMap(mapUid);

        _meta.SetEntityName(mapUid, $"Hilbert Hotel Room {code}");

        var landing = FindLanding(mapId, grids, template.LandingOffset);
        if (landing == null)
        {
            Log.Error($"Hilbert's Hotel could not find a landing spot in room map '{template.MapPath}'.");
            _map.DeleteMap(mapId);
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-load-failed"), user, user);
            return false;
        }

        var room = new HilbertHotelRoom
        {
            Code = code,
            Origin = teleporter,
            MapId = mapId,
            MapUid = mapUid,
            TemplateId = template.ID,
            Name = Loc.GetString(template.Name),
            OwnerId = userId,
            OwnerName = GetUserName(user, userId),
            Status = HilbertHotelRoomStatus.Open,
            Visible = true,
            Landing = landing.Value,
            DefaultReturn = _transform.GetMapCoordinates(teleporter),
        };

        EnsureFixtures(teleporterComp, mapUid, mapId, landing.Value);

        _rooms[code] = room;
        JoinRoom(room, user, userId);

        Log.Info($"Hilbert's Hotel: {room.OwnerName} checked into new room {code} ({template.ID}).");
        return true;
    }

    private void JoinRoom(HilbertHotelRoom room, EntityUid user, NetUserId userId)
    {
        // Cancel any pending empty-room timeout; the room is occupied again.
        room.EmptySince = null;

        room.EntryPoints[userId] = _transform.GetMapCoordinates(user);
        _transform.SetMapCoordinates(user, room.Landing);
        _popup.PopupEntity(Loc.GetString("hilbert-hotel-popup-welcome", ("code", room.Code)), user, user);
        NotifyRoomChanged(room);
    }

    #endregion

    #region Room controller

    /// <summary>
    /// Applies an action requested through a room controller. Only the room's owner
    /// may change anything.
    /// </summary>
    public bool TryRoomAction(
        EntityUid controller,
        EntityUid user,
        HilbertHotelRoomAction action,
        NetUserId targetUser,
        out LocId? error)
    {
        error = null;

        if (!TryComp<ActorComponent>(user, out var actor) ||
            !TryComp<HilbertHotelRoomControllerComponent>(controller, out var controllerComp) ||
            !TryGetRoomByMap(Transform(controller).MapID, out var room))
        {
            return false;
        }

        if (room.OwnerId != actor.PlayerSession.UserId)
        {
            error = "hilbert-hotel-error-not-owner";
            return false;
        }

        if (action == HilbertHotelRoomAction.DeleteRoom)
        {
            if (!DeleteRoom(room.Code, exemptUser: actor.PlayerSession.UserId))
            {
                error = "hilbert-hotel-error-occupied";
                return false;
            }

            return true;
        }

        switch (action)
        {
            case HilbertHotelRoomAction.CycleStatus:
                room.Status = room.Status switch
                {
                    HilbertHotelRoomStatus.Open => HilbertHotelRoomStatus.GuestsOnly,
                    HilbertHotelRoomStatus.GuestsOnly => HilbertHotelRoomStatus.Locked,
                    _ => HilbertHotelRoomStatus.Open,
                };
                break;

            case HilbertHotelRoomAction.ToggleVisibility:
                room.Visible = !room.Visible;
                break;

            case HilbertHotelRoomAction.AddGuest:
                if (targetUser == default || targetUser == room.OwnerId)
                    return false;
                if (!room.TrustedGuests.ContainsKey(targetUser) &&
                    room.TrustedGuests.Count >= controllerComp.MaxGuests)
                {
                    error = "hilbert-hotel-error-guest-limit";
                    return false;
                }
                room.TrustedGuests[targetUser] = GetUserName(null, targetUser);
                break;

            case HilbertHotelRoomAction.RemoveGuest:
                room.TrustedGuests.Remove(targetUser);
                break;

            case HilbertHotelRoomAction.ClearGuests:
                room.TrustedGuests.Clear();
                break;

            case HilbertHotelRoomAction.TransferOwnership:
                if (targetUser == default || targetUser == room.OwnerId)
                    return false;
                room.TrustedGuests.Remove(targetUser);
                room.OwnerId = targetUser;
                room.OwnerName = GetUserName(null, targetUser);
                break;

            default:
                return false;
        }

        NotifyRoomChanged(room);
        return true;
    }

    #endregion

    #region UI state

    public HilbertHotelTeleporterStateMessage BuildTeleporterState(EntityUid teleporter, NetUserId viewer)
    {
        var state = new HilbertHotelTeleporterStateMessage();

        foreach (var template in _prototypes.EnumeratePrototypes<HilbertHotelRoomPrototype>())
            state.Templates.Add(new HilbertHotelRoomTemplateEntry(template.ID, Loc.GetString(template.Name), template.Order));

        state.Templates.Sort((a, b) => a.Order != b.Order
            ? a.Order.CompareTo(b.Order)
            : string.CompareOrdinal(a.Id, b.Id));

        foreach (var room in _rooms.Values)
        {
            if (!room.Visible && room.OwnerId != viewer)
                continue;

            state.Rooms.Add(new HilbertHotelRoomEntry(
                room.Code,
                room.Name,
                room.OwnerName,
                room.Status,
                GetOccupants(room).Count,
                CanJoin(room, viewer),
                room.OwnerId == viewer));
        }

        state.Rooms.Sort((a, b) => a.Code.CompareTo(b.Code));
        state.RoomCount = CountRoomsFor(teleporter);
        state.MaxRooms = TryComp<HilbertHotelTeleporterComponent>(teleporter, out var comp) ? comp.MaxRooms : 0;
        return state;
    }

    public HilbertHotelRoomControllerStateMessage? BuildControllerState(EntityUid controller, NetUserId viewer)
    {
        if (!TryGetRoomByMap(Transform(controller).MapID, out var room))
            return null;

        var state = new HilbertHotelRoomControllerStateMessage
        {
            Code = room.Code,
            Status = room.Status,
            Visible = room.Visible,
            IsOwner = room.OwnerId == viewer,
            OwnerName = room.OwnerName,
        };

        foreach (var (userId, name) in room.TrustedGuests)
        {
            if (userId == room.OwnerId)
                continue;
            state.Guests.Add(new HilbertHotelGuestEntry(userId, name));
        }

        state.Guests.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        state.Occupants = GetOccupants(room);
        return state;
    }

    /// <summary>
    /// Sends fresh state to the given actor, or to every actor with this terminal open.
    /// </summary>
    public void UpdateTeleporterUi(EntityUid teleporter, EntityUid? actor = null)
    {
        if (!_ui.HasUi(teleporter, HilbertHotelTeleporterUiKey.Key))
            return;

        if (actor != null)
        {
            if (TryComp<ActorComponent>(actor, out var actorComp))
                _ui.ServerSendUiMessage(
                    teleporter,
                    HilbertHotelTeleporterUiKey.Key,
                    BuildTeleporterState(teleporter, actorComp.PlayerSession.UserId),
                    actor.Value);
            return;
        }

        foreach (var actorUid in _ui.GetActors(teleporter, HilbertHotelTeleporterUiKey.Key))
        {
            if (TryComp<ActorComponent>(actorUid, out var actorComp))
                _ui.ServerSendUiMessage(
                    teleporter,
                    HilbertHotelTeleporterUiKey.Key,
                    BuildTeleporterState(teleporter, actorComp.PlayerSession.UserId),
                    actorUid);
        }
    }

    /// <summary>
    /// Sends fresh state to the given actor, or to every actor with this controller open.
    /// </summary>
    public void UpdateControllerUi(EntityUid controller, EntityUid? actor = null)
    {
        if (!_ui.HasUi(controller, HilbertHotelRoomControllerUiKey.Key))
            return;

        if (actor != null)
        {
            if (!TryComp<ActorComponent>(actor, out var actorComp))
                return;
            var state = BuildControllerState(controller, actorComp.PlayerSession.UserId);
            if (state != null)
                _ui.ServerSendUiMessage(controller, HilbertHotelRoomControllerUiKey.Key, state, actor.Value);
            return;
        }

        foreach (var actorUid in _ui.GetActors(controller, HilbertHotelRoomControllerUiKey.Key))
        {
            if (!TryComp<ActorComponent>(actorUid, out var actorComp))
                continue;
            var state = BuildControllerState(controller, actorComp.PlayerSession.UserId);
            if (state != null)
                _ui.ServerSendUiMessage(controller, HilbertHotelRoomControllerUiKey.Key, state, actorUid);
        }
    }

    private void NotifyRoomChanged(HilbertHotelRoom room)
    {
        var teleporterQuery = EntityQueryEnumerator<HilbertHotelTeleporterComponent>();
        while (teleporterQuery.MoveNext(out var teleporter, out _))
        {
            UpdateTeleporterUi(teleporter);
        }

        var controllerQuery = EntityQueryEnumerator<HilbertHotelRoomControllerComponent, TransformComponent>();
        while (controllerQuery.MoveNext(out var controller, out _, out var xform))
        {
            if (xform.MapID == room.MapId)
                UpdateControllerUi(controller);
        }
    }

    #endregion

    #region Queries

    public bool TryGetRoomByMap(MapId mapId, [NotNullWhen(true)] out HilbertHotelRoom? room)
    {
        foreach (var candidate in _rooms.Values)
        {
            if (candidate.MapId != mapId)
                continue;
            room = candidate;
            return true;
        }

        room = null;
        return false;
    }

    /// <summary>
    /// Finds the room owned by the given player, if any.
    /// </summary>
    public bool TryGetOwnedRoom(NetUserId owner, [NotNullWhen(true)] out HilbertHotelRoom? room)
    {
        foreach (var candidate in _rooms.Values)
        {
            if (candidate.OwnerId != owner)
                continue;
            room = candidate;
            return true;
        }

        room = null;
        return false;
    }

    public bool CanJoin(HilbertHotelRoom room, NetUserId userId)
    {
        if (room.OwnerId == userId)
            return true;

        return room.Status switch
        {
            HilbertHotelRoomStatus.Open => true,
            HilbertHotelRoomStatus.GuestsOnly => room.TrustedGuests.ContainsKey(userId),
            _ => false,
        };
    }

    public int GetRoomCount()
    {
        return _rooms.Count;
    }

    /// <summary>
    /// The number of rooms created from a given check-in terminal (the per-terminal limit basis).
    /// </summary>
    private int CountRoomsFor(EntityUid teleporter)
    {
        var count = 0;
        foreach (var room in _rooms.Values)
        {
            if (room.Origin == teleporter)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Players (not ghosts) currently standing on the room's map.
    /// </summary>
    public List<HilbertHotelOccupantEntry> GetOccupants(HilbertHotelRoom room)
    {
        var occupants = new List<HilbertHotelOccupantEntry>();
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.MapID != room.MapId || HasComp<GhostComponent>(uid))
                continue;

            var userId = actor.PlayerSession.UserId;
            occupants.Add(new HilbertHotelOccupantEntry(
                MetaData(uid).EntityName,
                userId,
                room.TrustedGuests.ContainsKey(userId),
                room.OwnerId == userId));
        }

        occupants.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return occupants;
    }

    /// <summary>
    /// Whether any living mob (players, pets, etc.) is inside the room. Ghosts and
    /// dead bodies do not count. The exempt player is ignored so an owner can
    /// delete their own room while standing in it.
    /// </summary>
    private bool HasLivingOccupants(HilbertHotelRoom room, NetUserId? exemptUser = null)
    {
        var mobQuery = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobQuery.MoveNext(out var uid, out var mobState, out var xform))
        {
            if (xform.MapID != room.MapId || HasComp<GhostComponent>(uid))
                continue;
            if (mobState.CurrentState == MobState.Dead)
                continue;
            if (exemptUser != null &&
                TryComp<ActorComponent>(uid, out var mobActor) &&
                mobActor.PlayerSession.UserId == exemptUser)
            {
                continue;
            }

            return true;
        }

        // Players attached to non-mob entities still count.
        var actorQuery = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actorQuery.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.MapID != room.MapId || HasComp<GhostComponent>(uid))
                continue;
            if (exemptUser != null && actor.PlayerSession.UserId == exemptUser)
                continue;

            return true;
        }

        return false;
    }

    private HilbertHotelRoomPrototype? ResolveTemplate(string? templateId)
    {
        if (templateId != null && _prototypes.TryIndex(templateId, out HilbertHotelRoomPrototype? proto))
            return proto;

        HilbertHotelRoomPrototype? best = null;
        foreach (var candidate in _prototypes.EnumeratePrototypes<HilbertHotelRoomPrototype>())
        {
            if (best == null ||
                candidate.Order < best.Order ||
                (candidate.Order == best.Order && string.CompareOrdinal(candidate.ID, best.ID) < 0))
            {
                best = candidate;
            }
        }

        return best;
    }

    #endregion

    #region Map helpers

    private MapCoordinates? FindLanding(
        MapId mapId,
        HashSet<Entity<MapGridComponent>>? grids,
        Vector2 offset)
    {
        var markerQuery = EntityQueryEnumerator<HilbertHotelLandingMarkerComponent, TransformComponent>();
        while (markerQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;
            var coords = _transform.GetMapCoordinates(uid, xform);
            return new MapCoordinates(coords.Position + offset, coords.MapId);
        }

        EntityUid? lateJoin = null;
        EntityUid? anySpawn = null;
        var spawnQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            if (spawn.SpawnType == SpawnPointType.LateJoin)
            {
                lateJoin = uid;
                break;
            }

            if (spawn.SpawnType != SpawnPointType.Observer && anySpawn == null)
                anySpawn = uid;
        }

        if (lateJoin != null)
        {
            var coords = _transform.GetMapCoordinates(lateJoin.Value);
            return new MapCoordinates(coords.Position + offset, coords.MapId);
        }

        if (anySpawn != null)
        {
            var coords = _transform.GetMapCoordinates(anySpawn.Value);
            return new MapCoordinates(coords.Position + offset, coords.MapId);
        }

        if (grids is { Count: > 0 })
        {
            foreach (var grid in grids)
            {
                var bounds = _transform.GetWorldMatrix(grid.Owner).TransformBox(grid.Comp.LocalAABB);
                return new MapCoordinates(bounds.Center + offset, mapId);
            }
        }

        return offset == Vector2.Zero ? null : new MapCoordinates(offset, mapId);
    }

    private void EnsureFixtures(
        HilbertHotelTeleporterComponent teleporterComp,
        EntityUid mapUid,
        MapId mapId,
        MapCoordinates landing)
    {
        var hasExit = false;
        var hasController = false;

        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            if (HasComp<HilbertHotelExitComponent>(uid))
                hasExit = true;
            else if (HasComp<HilbertHotelRoomControllerComponent>(uid))
                hasController = true;
        }

        if (!hasExit)
            SpawnRoomFixture(teleporterComp.ExitPrototype, mapUid, landing.Position);

        if (!hasController)
            SpawnRoomFixture(teleporterComp.ControllerPrototype, mapUid, landing.Position + new Vector2(1f, 0f));
    }

    private void SpawnRoomFixture(EntProtoId proto, EntityUid mapUid, Vector2 position)
    {
        var ent = SpawnAtPosition(proto, new EntityCoordinates(mapUid, position));
        if (TryComp<TransformComponent>(ent, out var xform))
            _transform.AttachToGridOrMap(ent, xform);
    }

    /// <summary>
    /// Deletes a room and its map. Anything left behind is lost.
    /// </summary>
    /// <summary>
    /// Deletes a room and its map. Refuses while any living mob is inside unless
    /// <paramref name="force"/> is set; the exempt user (the owner deleting their
    /// own room) does not count.
    /// </summary>
    public bool DeleteRoom(int code, NetUserId? exemptUser = null, bool force = false)
    {
        if (!_rooms.TryGetValue(code, out var room))
            return false;

        if (!force && HasLivingOccupants(room, exemptUser))
            return false;

        _rooms.Remove(code);
        EjectOccupants(room);
        EvacuateGhosts(room);

        if (_map.MapExists(room.MapId))
            _map.DeleteMap(room.MapId);

        Log.Info($"Hilbert's Hotel: deleted room {code} ({room.MapId}).");

        var teleporterQuery = EntityQueryEnumerator<HilbertHotelTeleporterComponent>();
        while (teleporterQuery.MoveNext(out var teleporter, out _))
        {
            UpdateTeleporterUi(teleporter);
        }

        return true;
    }

    /// <summary>
    /// Deletes a room on behalf of its owner. Used by the check-in terminal's delete button.
    /// </summary>
    public bool TryDeleteRoomByOwner(EntityUid user, int code)
    {
        if (!TryComp<ActorComponent>(user, out var actor) ||
            !_rooms.TryGetValue(code, out var room))
        {
            return false;
        }

        if (room.OwnerId != actor.PlayerSession.UserId)
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-not-owner"), user, user);
            return false;
        }

        if (!DeleteRoom(code, exemptUser: actor.PlayerSession.UserId))
        {
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-error-occupied"), user, user);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sends any players inside a room back to where they checked in before it is deleted.
    /// </summary>
    private void EjectOccupants(HilbertHotelRoom room)
    {
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.MapID != room.MapId || HasComp<GhostComponent>(uid))
                continue;

            var destination = room.DefaultReturn;
            if (room.EntryPoints.TryGetValue(actor.PlayerSession.UserId, out var entry))
                destination = entry;

            if (!_map.MapExists(destination.MapId))
                destination = new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap);

            _transform.SetMapCoordinates(uid, destination);
            _popup.PopupEntity(Loc.GetString("hilbert-hotel-popup-room-deleted"), uid, uid);
        }
    }

    private void EvacuateGhosts(HilbertHotelRoom room)
    {
        var fallback = _map.MapExists(_gameTicker.DefaultMap)
            ? new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap)
            : new MapCoordinates(Vector2.Zero, room.DefaultReturn.MapId);

        var query = EntityQueryEnumerator<GhostComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == room.MapId)
                _transform.SetMapCoordinates(uid, fallback);
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _rooms.Clear();
        _nextRoomCreation.Clear();
        _nextSweep = TimeSpan.Zero;
    }

    #endregion

    private string GetUserName(EntityUid? user, NetUserId userId)
    {
        if (user is { Valid: true } uid)
            return MetaData(uid).EntityName;

        if (_playerManager.TryGetSessionById(userId, out var session))
        {
            if (session.AttachedEntity is { Valid: true } attached)
                return MetaData(attached).EntityName;
            return session.Name;
        }

        return userId.ToString();
    }
}
