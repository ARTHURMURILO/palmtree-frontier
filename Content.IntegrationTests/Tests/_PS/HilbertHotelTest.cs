using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.HilbertHotel;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Power.Components;
using Content.Shared._PS.HilbertHotel;
using Content.Shared._PS.HilbertHotel.Components;
using Content.Shared.Gravity;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: Hilbert's Hotel room maps must load at runtime with a landing spot,
/// exit door, room controller and breathable atmosphere.
/// </summary>
[TestFixture]
public sealed class HilbertHotelTest
{
    [Test]
    public async Task CheckInCreatesRoomAndExitReturnsPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var mindSys = server.System<SharedMindSystem>();
        var hotel = server.System<HilbertHotelSystem>();
        var testMap = await pair.CreateTestMap();

        var userId = playerMan.Sessions.Single().UserId;

        EntityUid human = default;
        EntityUid teleporter = default;

        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            teleporter = entMan.SpawnEntity("HilbertHotelTeleporter", testMap.GridCoords);
            var mindId = mindSys.CreateMind(userId);
            mindSys.TransferTo(mindId, human);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ActorComponent>(human), Is.True, "Test human has no player session attached.");

            Assert.That(hotel.TryCheckIn(teleporter, human, 4242, "CozyHotelRoom"), Is.True, "Check-in failed.");
            Assert.That(hotel.GetRoomCount(), Is.EqualTo(1));

            var roomMapId = entMan.GetComponent<TransformComponent>(human).MapID;
            Assert.That(roomMapId, Is.Not.EqualTo(testMap.MapId), "Player was not moved into the room map.");

            // The room must be breathable at the landing point.
            var atmosSystem = server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
            var roomAir = atmosSystem.GetTileMixture(human);
            Assert.That(roomAir, Is.Not.Null, "Room has no atmosphere at the landing point.");
            Assert.That(roomAir!.TotalMoles, Is.GreaterThan(0f), "Room is vacuum at the landing point.");

            // The room controller must exist and report the checking-in player as the owner.
            EntityUid controller = default;
            var controllerQuery = entMan.EntityQueryEnumerator<HilbertHotelRoomControllerComponent, TransformComponent>();
            while (controllerQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == roomMapId)
                    controller = uid;
            }

            Assert.That(controller, Is.Not.EqualTo(default(EntityUid)), "No room controller was spawned.");

            // The controller must never block a doorway or trap a player.
            Assert.That(entMan.GetComponent<FixturesComponent>(controller).Fixtures, Is.Empty);
            Assert.That(entMan.GetComponent<PhysicsComponent>(controller).CanCollide, Is.False);

            var state = hotel.BuildControllerState(controller, userId);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.Code, Is.EqualTo(4242));
            Assert.That(state.IsOwner, Is.True);

            // The exit door sends the player back to where they checked in.
            Assert.That(hotel.TryLeaveRoom(human), Is.True, "Leaving the room failed.");
            Assert.That(entMan.GetComponent<TransformComponent>(human).MapID, Is.EqualTo(testMap.MapId));

            Assert.That(hotel.DeleteRoom(4242), Is.True);
            Assert.That(hotel.GetRoomCount(), Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OneRoomPerOwnerAndTerminalDelete()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var mindSys = server.System<SharedMindSystem>();
        var hotel = server.System<HilbertHotelSystem>();
        var testMap = await pair.CreateTestMap();

        var userId = playerMan.Sessions.Single().UserId;

        EntityUid human = default;
        EntityUid teleporter = default;

        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            teleporter = entMan.SpawnEntity("HilbertHotelTeleporter", testMap.GridCoords);
            var mindId = mindSys.CreateMind(userId);
            mindSys.TransferTo(mindId, human);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(hotel.TryCheckIn(teleporter, human, 222, "CozyHotelRoom"), Is.True);

            // A player may only own one room at a time.
            Assert.That(hotel.TryCheckIn(teleporter, human, 333, "CozyHotelRoom"), Is.False);
            Assert.That(hotel.GetRoomCount(), Is.EqualTo(1));

            // The terminal's delete path works for the owner.
            Assert.That(hotel.TryDeleteRoomByOwner(human, 222), Is.True);
            Assert.That(hotel.GetRoomCount(), Is.EqualTo(0));

            // An immediate delete/recreate cycle is refused by the terminal's creation cooldown...
            Assert.That(hotel.TryCheckIn(teleporter, human, 333, "CozyHotelRoom"), Is.False,
                "The creation cooldown should refuse an immediate recreate.");

            // ...and with the cooldown out of the way, a new room can be created again.
            entMan.GetComponent<HilbertHotelTeleporterComponent>(teleporter).CreateCooldown = TimeSpan.Zero;
            Assert.That(hotel.TryCheckIn(teleporter, human, 333, "CozyHotelRoom"), Is.True);
            Assert.That(hotel.TryDeleteRoomByOwner(human, 333), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllRoomArchetypesLoadAndHoldAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapLoader = server.System<MapLoaderSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var atmos = server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();

        var roomIds = protoMan.EnumeratePrototypes<HilbertHotelRoomPrototype>().Select(p => p.ID).ToList();
        var mapIds = new List<MapId>();
        var gridUids = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in roomIds)
            {
                var proto = protoMan.Index<HilbertHotelRoomPrototype>(id);
                var options = DeserializationOptions.Default with { InitializeMaps = true };
                Assert.That(
                    mapLoader.TryLoadMap(proto.MapPath, out var map, out var grids, options),
                    Is.True,
                    $"Failed to load {id} ({proto.MapPath}).");
                mapIds.Add(map!.Value.Comp.MapId);
                gridUids.Add(grids!.First().Owner);
            }
        });

        try
        {
            // Let the atmosphere simulation run; a room with a gap would depressurize.
            await pair.RunTicksSync(200);

            await server.WaitAssertion(() =>
            {
                for (var i = 0; i < roomIds.Count; i++)
                {
                    var id = roomIds[i];
                    var mapId = mapIds[i];

                    EntityUid? probe = null;
                    var markerQuery = entMan.EntityQueryEnumerator<HilbertHotelLandingMarkerComponent, TransformComponent>();
                    while (markerQuery.MoveNext(out var uid, out _, out var xform))
                    {
                        if (xform.MapID == mapId)
                        {
                            probe = uid;
                            break;
                        }
                    }

                    if (probe == null)
                    {
                        var spawnQuery = entMan.EntityQueryEnumerator<Content.Server.Spawners.Components.SpawnPointComponent, TransformComponent>();
                        while (spawnQuery.MoveNext(out var uid, out var spawn, out var xform))
                        {
                            if (xform.MapID == mapId && spawn.SpawnType == Content.Server.Spawners.Components.SpawnPointType.LateJoin)
                            {
                                probe = uid;
                                break;
                            }
                        }
                    }

                    Assert.That(probe, Is.Not.Null, $"{id}: no landing point found.");
                    var landingCoords = entMan.GetComponent<TransformComponent>(probe!.Value).Coordinates;
                    var air = atmos.GetTileMixture(probe.Value);
                    Assert.That(air, Is.Not.Null, $"{id}: no atmosphere at the landing point.");
                    Assert.That(air!.Pressure, Is.GreaterThan(20f), $"{id}: the room leaked out to vacuum.");

                    // The controller must not share the arrival tile.
                    var landingControllerQuery = entMan.EntityQueryEnumerator<HilbertHotelRoomControllerComponent, TransformComponent>();
                    while (landingControllerQuery.MoveNext(out _, out _, out var landingControllerXform))
                    {
                        if (landingControllerXform.MapID != mapId)
                            continue;
                        Assert.That(
                            landingControllerXform.Coordinates,
                            Is.Not.EqualTo(landingCoords),
                            $"{id}: room controller overlaps the landing point.");
                    }

                    // Every room must have gravity, otherwise players float.
                    var gravity = entMan.GetComponent<GravityComponent>(gridUids[i]);
                    Assert.That(gravity.Enabled, Is.True, $"{id}: room grid has gravity disabled.");

                    // The debug station is a real map with intentionally loose props; the
                    // strict hotel-room checks below only apply to the authored room maps.
                    if (id == "DebugStationHotelRoom")
                        continue;

                    // Every prop placed in the room map must be anchored to the floor.
                    var propQuery = entMan.EntityQueryEnumerator<TransformComponent>();
                    while (propQuery.MoveNext(out var prop, out var propXform))
                    {
                        if (propXform.MapID != mapId || propXform.Anchored)
                            continue;
                        // Only check entities parented directly to the grid; entities
                        // nested in containers (circuitboards, actions) are not props.
                        if (propXform.ParentUid != gridUids[i])
                            continue;

                        var protoId = entMan.GetComponent<MetaDataComponent>(prop).EntityPrototype?.ID ?? prop.ToString();
                        Assert.Fail($"{id}: '{protoId}' at {propXform.Coordinates} is unanchored.");
                    }

                    // Machines with an APC receiver must be flagged self-powered.
                    var powerQuery = entMan.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
                    while (powerQuery.MoveNext(out var powerUid, out var receiver, out var powerXform))
                    {
                        if (powerXform.MapID != mapId)
                            continue;
                        var receiverProto = entMan.GetComponent<MetaDataComponent>(powerUid).EntityPrototype?.ID ?? "?";
                        Assert.That(receiver.NeedsPower, Is.False, $"{id}: '{receiverProto}' still requires APC power.");
                    }
                }
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                foreach (var mapId in mapIds)
                {
                    if (mapSystem.MapExists(mapId))
                        mapSystem.DeleteMap(mapId);
                }
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GhostReturnDoesNotFreezeRoom()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var mindSys = server.System<SharedMindSystem>();
        var hotel = server.System<HilbertHotelSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        var userId = playerMan.Sessions.Single().UserId;

        EntityUid human = default;
        EntityUid teleporter = default;
        EntityUid mindId = default;

        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            teleporter = entMan.SpawnEntity("HilbertHotelTeleporter", testMap.GridCoords);
            mindId = mindSys.CreateMind(userId);
            mindSys.TransferTo(mindId, human);
        });

        await pair.RunTicksSync(5);

        MapId roomMapId = default;
        EntityUid body = human;

        await server.WaitAssertion(() =>
        {
            Assert.That(hotel.TryCheckIn(teleporter, body, 999, "CozyHotelRoom"), Is.True);
            roomMapId = entMan.GetComponent<TransformComponent>(body).MapID;
        });

        // Ghost out of the room and let the occupancy sweep notice the room is empty.
        await server.WaitPost(() =>
        {
            var ghost = entMan.SpawnEntity(GameTicker.ObserverPrototypeName, MapCoordinates.Nullspace);
            mindSys.TransferTo(mindId, ghost);
        });

        await pair.RunTicksSync(200);

        // Return to the body; the map must still be live and the body must not be paused.
        await server.WaitPost(() => mindSys.TransferTo(mindId, body));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(hotel.GetRoomCount(), Is.EqualTo(1), "Room was deleted while the owner was a ghost.");
            Assert.That(mapSystem.IsPaused(roomMapId), Is.False, "Room map was left paused after returning to the body.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(body).EntityPaused, Is.False, "Player body was left frozen.");
            Assert.That(entMan.GetComponent<TransformComponent>(body).MapID, Is.EqualTo(roomMapId));
        });

        await server.WaitPost(() =>
        {
            if (hotel.GetRoomCount() > 0)
                hotel.TryDeleteRoomByOwner(body, 999);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OccupiedRoomsCannotBeDeleted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var mindSys = server.System<SharedMindSystem>();
        var hotel = server.System<HilbertHotelSystem>();
        var testMap = await pair.CreateTestMap();

        var userId = playerMan.Sessions.Single().UserId;

        EntityUid human = default;
        EntityUid teleporter = default;

        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            teleporter = entMan.SpawnEntity("HilbertHotelTeleporter", testMap.GridCoords);
            var mindId = mindSys.CreateMind(userId);
            mindSys.TransferTo(mindId, human);
        });

        await pair.RunTicksSync(5);

        EntityUid cat = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(hotel.TryCheckIn(teleporter, human, 555, "CozyHotelRoom"), Is.True);

            // A living pet inside blocks deletion, even for the room's owner.
            cat = entMan.SpawnEntity("MobCat", entMan.GetComponent<TransformComponent>(human).Coordinates);
            Assert.That(hotel.TryDeleteRoomByOwner(human, 555), Is.False, "Owner deleted a room with a cat inside.");
            Assert.That(hotel.DeleteRoom(555), Is.False, "Room with a living mob inside was deleted.");
        });

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(cat);
            Assert.That(hotel.TryLeaveRoom(human), Is.True);
        });

        // A cat alone inside keeps the room alive even past the empty timeout.
        EntityUid cat2 = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(hotel.TryGetOwnedRoom(userId, out var room), Is.True);
            cat2 = entMan.SpawnEntity("MobCat", room!.Landing);
            room.EmptySince = timing.CurTime - TimeSpan.FromMinutes(6);
        });

        await pair.RunTicksSync(200);

        await server.WaitAssertion(() =>
        {
            Assert.That(hotel.GetRoomCount(), Is.EqualTo(1), "Room with a cat inside was auto-deleted.");

            // With the room truly empty, the expired timer deletes it on the next sweep.
            entMan.DeleteEntity(cat2);
            Assert.That(hotel.TryGetOwnedRoom(userId, out var room), Is.True);
            room!.EmptySince = timing.CurTime - TimeSpan.FromMinutes(6);
        });

        await pair.RunTicksSync(200);

        await server.WaitAssertion(() =>
        {
            Assert.That(hotel.GetRoomCount(), Is.EqualTo(0), "Empty room was not auto-deleted after the timeout.");
        });

        await pair.CleanReturnAsync();
    }
}
