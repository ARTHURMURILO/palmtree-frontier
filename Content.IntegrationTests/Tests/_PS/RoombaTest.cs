using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.NPC.Systems;
using Content.Shared._PS.Roomba;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the roomba drives around like a real robot vacuum - it must keep moving, never leave
/// its grid into space, scrub cleanable decals (semen drips, grime, crayon) it drives over while
/// leaving decals that are not cleanable alone, and vacuum loose trash into its dustbin.
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
            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            roombaComp.Enabled = true;
            npc.WakeNPC(roomba);

            // Smoke-test the speech path: force one idle line immediately.
            roombaComp.NextIdleSpeech = TimeSpan.Zero;
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
    /// The roomba vacuums loose trash into its internal dustbin, using the trash bag whitelist, but
    /// leaves useful items alone.
    /// </summary>
    [Test]
    public async Task VacuumsUpTrashButNotTools()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var coords = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));

        EntityUid roomba = default;
        EntityUid trash = default;
        EntityUid crowbar = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var npc = server.System<NPCSystem>();

            roomba = entMan.SpawnEntity("MobRoomba", coords);
            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            roombaComp.Enabled = true;
            npc.WakeNPC(roomba);
            trash = entMan.SpawnEntity("TrashBananaPeel", coords);
            crowbar = entMan.SpawnEntity("Crowbar", coords);

            // Force a scrub pass on the next tick even though the roomba has nowhere to drive yet.
            roombaComp.LastScrubPosition = coords.Position + new Vector2(2f, 0f);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True, "the roomba should have an internal trash bag");

            var bag = bagContainer.ContainedEntities.Single();
            var storage = entMan.GetComponent<StorageComponent>(bag);

            Assert.Multiple(() =>
            {
                Assert.That(storage.Container.ContainedEntities, Does.Contain(trash),
                    "the roomba should vacuum up trash");
                Assert.That(storage.Container.ContainedEntities, Does.Not.Contain(crowbar),
                    "the roomba should not vacuum up tools");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Taking the full bag from the right-click menu gives it to the player and installs a fresh
    /// empty bag. Left-clicking the roomba only switches it on or off.
    /// </summary>
    [Test]
    public async Task TakeTrashBagVerbSwapsFullBagForEmptyOne()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var coords = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));

        EntityUid roomba = default;
        EntityUid player = default;
        EntityUid fullBag = default;
        EntityUid trash = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var npc = server.System<NPCSystem>();

            roomba = entMan.SpawnEntity("MobRoomba", coords);
            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            roombaComp.Enabled = true;
            npc.WakeNPC(roomba);
            player = entMan.SpawnEntity("MobHuman", coords);
            trash = entMan.SpawnEntity("TrashBananaPeel", coords);

            roombaComp.LastScrubPosition = coords.Position + new Vector2(2f, 0f);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True, "the roomba should have an internal trash bag");

            fullBag = bagContainer.ContainedEntities.Single();
            Assert.That(entMan.GetComponent<StorageComponent>(fullBag).Container.ContainedEntities,
                Does.Contain(trash), "the bag should contain the collected trash");

            var verbs = GetVerbs(entMan, player, roomba);
            var takeBag = verbs.SingleOrDefault(v => v.Text == Loc.GetString("roomba-verb-take-trash-bag"));
            Assert.That(takeBag, Is.Not.Null, "a full bag should offer a take-bag verb");
            Assert.That(takeBag!.Disabled, Is.False, "the player has a free hand");
            takeBag.Act!();
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var hands = server.System<SharedHandsSystem>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(hands.EnumerateHeld(player), Does.Contain(fullBag),
                "the player should be holding the full bag");
            Assert.That(entMan.GetComponent<StorageComponent>(fullBag).Container.ContainedEntities,
                Does.Contain(trash), "the full bag should keep the trash it collected");

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True, "the roomba should have a replacement bag");

            var replacement = bagContainer.ContainedEntities.Single();
            Assert.That(replacement, Is.Not.EqualTo(fullBag), "the replacement must be a new bag");
            Assert.That(entMan.GetComponent<StorageComponent>(replacement).Container.ContainedEntities,
                Is.Empty, "the replacement bag must be empty");
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

            // Clicking toggles power instead of petting, even in client-side prediction.
            Assert.That(proto.TryGetComponent<InteractionPopupComponent>(out var popup, factory), Is.True);
            Assert.That(popup!.InteractSuccessString, Is.Null);
            Assert.That(popup.InteractFailureString, Is.Null);
            Assert.That(popup.InteractSuccessSound, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Roombas arrive switched off. Left-clicking (or activating) one toggles it, and an off roomba
    /// must not move, clean or collect anything.
    /// </summary>
    [Test]
    public async Task SpawnsDeactivatedAndClickTogglesPower()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var coords = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));

        EntityUid roomba = default;
        EntityUid player = default;
        EntityUid trash = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            roomba = entMan.SpawnEntity("MobRoomba", coords);
            player = entMan.SpawnEntity("MobHuman", coords);
            trash = entMan.SpawnEntity("TrashBananaPeel", coords);

            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            roombaComp.LastScrubPosition = coords.Position + new Vector2(2f, 0f);

            Assert.That(roombaComp.Enabled, Is.False, "roombas should spawn deactivated");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True);
            var bag = bagContainer.ContainedEntities.Single();
            Assert.That(entMan.GetComponent<StorageComponent>(bag).Container.ContainedEntities, Is.Empty,
                "a switched-off roomba must not clean or collect trash");
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            entMan.EventBus.RaiseLocalEvent(roomba, new InteractHandEvent(player, roomba));

            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            Assert.That(roombaComp.Enabled, Is.True, "clicking the roomba should switch it on");
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(roomba), Is.True,
                "switching it on should wake the NPC");

            // Force a scrub pass now that it is running.
            roombaComp.LastScrubPosition = coords.Position + new Vector2(2f, 0f);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True);
            var bag = bagContainer.ContainedEntities.Single();
            Assert.That(entMan.GetComponent<StorageComponent>(bag).Container.ContainedEntities, Does.Contain(trash),
                "a switched-on roomba should collect trash again");
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            entMan.EventBus.RaiseLocalEvent(roomba, new InteractHandEvent(player, roomba));

            Assert.That(entMan.GetComponent<RoombaComponent>(roomba).Enabled, Is.False,
                "clicking the roomba again should switch it off");
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(roomba), Is.False,
                "switching it off should put the NPC to sleep");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Trash sitting on a table shares the table tile's coordinates but is not on the floor, so the
    /// roomba must leave it alone (and still vacuum the floor trash next to the table).
    /// </summary>
    [Test]
    public async Task DoesNotVacuumTrashOnTables()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var grid = testMap.Grid;
        var gridCoords = testMap.GridCoords;
        var roombaCoords = gridCoords.Offset(new Vector2(0.5f, 0.5f));
        var tableCoords = gridCoords.Offset(new Vector2(1.5f, 0.5f));

        EntityUid roomba = default;
        EntityUid tableTrash = default;
        EntityUid floorTrash = default;

        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            var tile = new Tile(testMap.Tile.Tile.TypeId, testMap.Tile.Tile.Flags, testMap.Tile.Tile.Variant);
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    map.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
                }
            }
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var npc = server.System<NPCSystem>();

            entMan.SpawnEntity("Table", tableCoords);
            tableTrash = entMan.SpawnEntity("TrashBananaPeel", tableCoords);
            floorTrash = entMan.SpawnEntity("TrashBananaPeel", roombaCoords);

            roomba = entMan.SpawnEntity("MobRoomba", roombaCoords);
            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            roombaComp.Enabled = true;
            roombaComp.TrashPickupRadius = 2f; // reach the table without standing on it
            npc.WakeNPC(roomba);
            roombaComp.LastScrubPosition = roombaCoords.Position + new Vector2(2f, 0f);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True);
            var bag = bagContainer.ContainedEntities.Single();
            var stored = entMan.GetComponent<StorageComponent>(bag).Container.ContainedEntities;

            Assert.That(stored, Does.Contain(floorTrash), "the roomba should vacuum the floor trash");
            Assert.That(stored, Does.Not.Contain(tableTrash), "the roomba must not vacuum trash off a table");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The right-click menu can stop trash collection and mute the roomba's voice; both are modes,
    /// so the roomba keeps scrubbing and driving while they are toggled.
    /// </summary>
    [Test]
    public async Task TrashAndMuteVerbsToggleModes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var coords = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));

        EntityUid roomba = default;
        EntityUid player = default;
        EntityUid trash = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var npc = server.System<NPCSystem>();

            roomba = entMan.SpawnEntity("MobRoomba", coords);
            player = entMan.SpawnEntity("MobHuman", coords);

            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            roombaComp.Enabled = true;
            roombaComp.TrashPickupRadius = 2f;
            npc.WakeNPC(roomba);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);

            var disableTrash = GetVerbs(entMan, player, roomba)
                .SingleOrDefault(v => v.Text == Loc.GetString("roomba-verb-trash-off"));
            Assert.That(disableTrash, Is.Not.Null);
            disableTrash!.Act!();

            Assert.That(roombaComp.CollectTrash, Is.False, "the trash-collection verb should switch it off");

            trash = entMan.SpawnEntity("TrashBananaPeel", coords);
            roombaComp.LastScrubPosition = coords.Position + new Vector2(2f, 0f);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True);
            var bag = bagContainer.ContainedEntities.Single();
            Assert.That(entMan.GetComponent<StorageComponent>(bag).Container.ContainedEntities, Is.Empty,
                "trash collection is off, so nothing should be vacuumed");

            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            var enableTrash = GetVerbs(entMan, player, roomba)
                .SingleOrDefault(v => v.Text == Loc.GetString("roomba-verb-trash-on"));
            Assert.That(enableTrash, Is.Not.Null);
            enableTrash!.Act!();

            Assert.That(roombaComp.CollectTrash, Is.True, "the trash-collection verb should switch it back on");
            roombaComp.LastScrubPosition = coords.Position + new Vector2(2f, 0f);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containers = server.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(roomba, RoombaComponent.TrashBagContainerId,
                out var bagContainer), Is.True);
            var bag = bagContainer.ContainedEntities.Single();
            Assert.That(entMan.GetComponent<StorageComponent>(bag).Container.ContainedEntities, Does.Contain(trash),
                "trash collection is on again, so the trash should be vacuumed");

            var roombaComp = entMan.GetComponent<RoombaComponent>(roomba);
            var mute = GetVerbs(entMan, player, roomba)
                .SingleOrDefault(v => v.Text == Loc.GetString("roomba-verb-mute"));
            Assert.That(mute, Is.Not.Null);
            mute!.Act!();
            Assert.That(roombaComp.MuteSpeech, Is.True, "the mute verb should mute the roomba");

            var unmute = GetVerbs(entMan, player, roomba)
                .SingleOrDefault(v => v.Text == Loc.GetString("roomba-verb-unmute"));
            Assert.That(unmute, Is.Not.Null);
            unmute!.Act!();
            Assert.That(roombaComp.MuteSpeech, Is.False, "the unmute verb should unmute the roomba");
        });

        await pair.CleanReturnAsync();
    }

    private static List<Verb> GetVerbs(IEntityManager entMan, EntityUid user, EntityUid target)
    {
        var ev = new GetVerbsEvent<Verb>(user, target, null, null, true, true, true, new List<VerbCategory>());
        entMan.EventBus.RaiseLocalEvent(target, ev);
        return ev.Verbs.ToList();
    }
}
