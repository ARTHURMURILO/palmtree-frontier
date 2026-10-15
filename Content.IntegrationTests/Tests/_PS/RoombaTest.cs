using System.Collections.Generic;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the roomba's decal-scrubbing HTN branch removes cleanable decals (semen drips, grime,
/// crayon) and leaves decals that are not cleanable alone.
/// </summary>
[TestFixture]
public sealed class RoombaTest
{
    [Test]
    public async Task ScrubsCleanableDecals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var grid = testMap.Grid.Owner;
        var mobCoords = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));
        var cleanableCoords = testMap.GridCoords.Offset(new Vector2(0.25f, 0.25f));
        var protectedCoords = testMap.GridCoords.Offset(new Vector2(0.6f, 0.6f));

        uint cleanableId = 0;
        uint protectedId = 0;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var decals = server.System<DecalSystem>();
            var npc = server.System<NPCSystem>();

            // One cleanable decal (space cleaner would remove it) and one that is not.
            Assert.That(decals.TryAddDecal("SemenDrip1", cleanableCoords, out cleanableId, cleanable: true), Is.True);
            Assert.That(decals.TryAddDecal("SemenPuddle1", protectedCoords, out protectedId, cleanable: false), Is.True);

            var roomba = entMan.SpawnEntity("MobRoomba", mobCoords);
            npc.WakeNPC(roomba);
        });

        // Let the HTN plan, drive over the drip and scrub it.
        await server.WaitRunTicks(600);

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            var remaining = decals.GetDecalsInRange(grid, testMap.GridCoords.Position, 2f);
            var remainingIds = new List<uint>();
            foreach (var (index, _) in remaining)
            {
                remainingIds.Add(index);
            }

            Assert.That(remainingIds, Does.Not.Contain(cleanableId), "the cleanable decal should have been scrubbed");
            Assert.That(remainingIds, Does.Contain(protectedId), "decals that are not cleanable must be left alone");
        });

        await pair.CleanReturnAsync();
    }
}
