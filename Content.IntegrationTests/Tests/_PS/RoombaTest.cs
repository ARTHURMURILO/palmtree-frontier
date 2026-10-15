using System.Collections.Generic;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.NPC.Systems;
using Content.Shared._PS.Roomba;
using Content.Shared.Maps;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the roomba drives around like a real robot vacuum - it must keep moving, never leave
/// its grid into space, and scrub cleanable decals (semen drips, grime, crayon) it drives over while
/// leaving decals that are not cleanable alone.
/// </summary>
[TestFixture]
public sealed class RoombaTest
{
    [Test]
    public async Task RoamsAndScrubsDecalsWithoutLeavingTheGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var grid = testMap.Grid;
        var spawnCoords = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));
        var cleanableCoords = testMap.GridCoords.Offset(new Vector2(0.25f, 0.25f));
        var protectedCoords = testMap.GridCoords.Offset(new Vector2(0.6f, 0.6f));

        EntityUid roomba = default;
        uint cleanableId = 0;
        uint protectedId = 0;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var map = server.System<SharedMapSystem>();
            var decals = server.System<DecalSystem>();
            var npc = server.System<NPCSystem>();

            // Grow the one-tile test map into a 5x5 room so the roomba has floor to roam.
            var tile = new Tile(testMap.Tile.Tile.TypeId, testMap.Tile.Tile.Flags, testMap.Tile.Tile.Variant);
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    map.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
                }
            }

            // One cleanable decal (space cleaner would remove it) and one that is not.
            Assert.That(decals.TryAddDecal("SemenDrip1", cleanableCoords, out cleanableId, cleanable: true), Is.True);
            Assert.That(decals.TryAddDecal("SemenPuddle1", protectedCoords, out protectedId, cleanable: false), Is.True);

            roomba = entMan.SpawnEntity("MobRoomba", spawnCoords);
            npc.WakeNPC(roomba);

            // Smoke-test the speech path: force one idle line immediately.
            entMan.GetComponent<RoombaComponent>(roomba).NextIdleSpeech = TimeSpan.Zero;
        });

        // Roam for a while, checking every second that it is still on solid floor.
        var trace = new List<string>();
        for (var i = 0; i < 15; i++)
        {
            await server.WaitRunTicks(30);

            var iteration = i;
            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var map = server.System<SharedMapSystem>();
                var turf = server.System<TurfSystem>();

                var xform = entMan.GetComponent<TransformComponent>(roomba);
                trace.Add($"{iteration}: pos={xform.LocalPosition} grid={xform.GridUid} parent={xform.ParentUid} map={xform.MapUid}");
                Assert.That(xform.GridUid, Is.EqualTo(grid.Owner), string.Join("\n", trace));

                Assert.That(map.TryGetTileRef(grid.Owner, grid.Comp, xform.Coordinates, out var tileRef), Is.True);
                Assert.That(tileRef.Tile.IsEmpty, Is.False, "the roomba must not drive over space");
                Assert.That(turf.IsSpace(tileRef), Is.False, "the roomba must not drive over space");
            });
        }

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var decals = server.System<DecalSystem>();

            var remaining = decals.GetDecalsInRange(grid.Owner, testMap.GridCoords.Position, 2f);
            var remainingIds = new List<uint>();
            foreach (var (index, _) in remaining)
            {
                remainingIds.Add(index);
            }

            Assert.That(remainingIds, Does.Not.Contain(cleanableId), "the cleanable decal should have been scrubbed");
            Assert.That(remainingIds, Does.Contain(protectedId), "decals that are not cleanable must be left alone");

            var position = entMan.GetComponent<TransformComponent>(roomba).LocalPosition;
            Assert.That((position - spawnCoords.Position).Length(), Is.GreaterThan(0.5f),
                "the roomba should have driven around");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The roomba rolls under tables (its fixtures use the small-mob layers) and never shoves mobs:
    /// the mob-collision target side is cancelled for it on both sides.
    /// </summary>
    [Test]
    public async Task FitsUnderTablesAndDoesNotPushMobs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var grid = testMap.Grid;
        var roombaCoords = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));
        var tableCoords = testMap.GridCoords.Offset(new Vector2(1.5f, 0.5f));

        EntityUid roomba = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            var table = entMan.SpawnEntity("Table", tableCoords);
            roomba = entMan.SpawnEntity("MobRoomba", roombaCoords);

            // Tables do not collide with the roomba: it goes under them.
            var physics = server.System<SharedPhysicsSystem>();
            Assert.That(
                physics.IsCurrentlyHardCollidable((roomba, null, null), (table, null, null)),
                Is.False,
                "the roomba must fit under tables");

            // The roomba refuses to be a mob-push target, so nobody gets shoved out of its way.
            var targetEv = new AttemptMobTargetCollideEvent();
            entMan.EventBus.RaiseLocalEvent(roomba, ref targetEv);
            Assert.That(targetEv.Cancelled, Is.True, "the roomba must not push mobs around");
        });

        // ...and it must also be drawn underneath tables, not over them. Sprites only exist
        // client-side, so read the prototype's sprite component from the client.
        await pair.Client.WaitAssertion(() =>
        {
            var protoMan = pair.Client.ResolveDependency<IPrototypeManager>();
            var factory = pair.Client.ResolveDependency<IComponentFactory>();
            var proto = protoMan.Index<EntityPrototype>("MobRoomba");

            Assert.That(proto.TryGetComponent<SpriteComponent>(out var sprite, factory), Is.True);
            Assert.That(sprite!.DrawDepth, Is.LessThan((int) Content.Shared.DrawDepth.DrawDepth.Objects),
                "the roomba must render under tables");
        });

        await pair.CleanReturnAsync();
    }
}
