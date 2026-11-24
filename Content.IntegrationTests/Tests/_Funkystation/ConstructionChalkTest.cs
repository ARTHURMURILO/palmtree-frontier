// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._Funkystation.ConstructionChalk;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using NUnit.Framework;
using Robust.Server.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Guards the construction chalk flow: a placed mark remembers its recipe, and using a
///     construction material on it starts the normal construction do-after at the mark's tile.
/// </summary>
[TestFixture]
public sealed class ConstructionChalkTest
{
    [Test]
    public async Task ChalkBoxContainsThreeChalk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = server.System<ContainerSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var box = entMan.SpawnEntity("ConstructionChalkBox", testMap.GridCoords);
            Assert.That(containers.TryGetContainer(box, StorageComponent.ContainerId, out var storage), Is.True,
                "the chalk box should have a storage container");
            Assert.That(storage!.ContainedEntities.Count, Is.EqualTo(3),
                "the chalk box should spawn with three pieces of chalk");
            Assert.That(storage.ContainedEntities.All(entMan.HasComponent<ConstructionChalkComponent>), Is.True,
                "everything in the chalk box should be construction chalk");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChalkMarkPlacementIsCappedAndMarksExpire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var chalkSystem = server.System<Content.Server._Funkystation.ConstructionChalk.ConstructionChalkSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var plating = tiles["Plating"];
            for (var x = 0; x <= 6; x++)
            {
                for (var y = 0; y <= 6; y++)
                    mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            // The cap is global, so start from a clean slate.
            var existingMarks = new List<EntityUid>();
            var markQuery = entMan.EntityQueryEnumerator<ConstructionChalkMarkComponent>();
            while (markQuery.MoveNext(out var markUid, out _))
                existingMarks.Add(markUid);
            foreach (var markUid in existingMarks)
                entMan.DeleteEntity(markUid);

            var userCoords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(3.5f, 3.5f));
            var user = entMan.SpawnEntity("MobHuman", userCoords);
            var chalk = entMan.SpawnEntity("ConstructionChalk", userCoords);
            var chalkComp = entMan.GetComponent<ConstructionChalkComponent>(chalk);
            chalkComp.MaxMarks = 2;
            chalkComp.MarkLifetime = TimeSpan.FromMinutes(5);

            var recipe = protoMan.Index<ConstructionPrototype>("Girder");

            EntityCoordinates MarkAt(int x, int y) =>
                new(testMap.Grid.Owner, new Vector2(x + 0.5f, y + 0.5f));

            Assert.Multiple(() =>
            {
                Assert.That(chalkSystem.TryPlaceMark(user, chalkComp, MarkAt(2, 3), recipe, Angle.Zero), Is.True);
                Assert.That(chalkSystem.TryPlaceMark(user, chalkComp, MarkAt(4, 3), recipe, Angle.Zero), Is.True);
            });

            Assert.That(CountMarks(entMan), Is.EqualTo(2), "both allowed marks should have spawned");

            // One past the cap is rejected and must not spawn an entity.
            Assert.That(chalkSystem.TryPlaceMark(user, chalkComp, MarkAt(3, 2), recipe, Angle.Zero), Is.False,
                "placing a mark past MaxMarks must be rejected");
            Assert.That(CountMarks(entMan), Is.EqualTo(2), "a rejected mark must not spawn an entity");

            // Marks expire through the chalk's configured lifetime.
            var marks = new List<EntityUid>();
            var expiringQuery = entMan.EntityQueryEnumerator<ConstructionChalkMarkComponent>();
            while (expiringQuery.MoveNext(out var markUid, out _))
                marks.Add(markUid);

            foreach (var markUid in marks)
            {
                Assert.That(entMan.TryGetComponent<TimedDespawnComponent>(markUid, out var despawn), Is.True,
                    "chalk marks should expire on a timer");
                Assert.That(despawn!.Lifetime, Is.EqualTo(300f).Within(0.01f));
            }
        });

        await pair.CleanReturnAsync();
    }

    private static int CountMarks(IEntityManager entMan)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<ConstructionChalkMarkComponent>();
        while (query.MoveNext(out _, out _))
            count++;

        return count;
    }

    [Test]
    public async Task ChalkMarkBuildsPlannedStructure()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        var hands = server.System<SharedHandsSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid user = default;
        EntityUid steel = default;

        await server.WaitAssertion(() =>
        {
            var plating = tiles["Plating"];
            for (var x = 0; x <= 3; x++)
            {
                mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), new Tile(plating.TileId));
            }

            var userCoords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(1.5f, 0.5f));
            user = entMan.SpawnEntity("MobHuman", userCoords);
            steel = entMan.SpawnEntity("SheetSteel", userCoords);
            Assert.That(hands.TryPickupAnyHand(user, steel), Is.True,
                "Test setup: the user needs to hold a construction material.");

            var markCoords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(2.5f, 0.5f));
            var mark = entMan.SpawnEntity("ConstructionChalkMark", markCoords);
            var markComp = entMan.GetComponent<ConstructionChalkMarkComponent>(mark);
            markComp.ConstructionPrototype = new ProtoId<ConstructionPrototype>("Girder");

            entMan.EventBus.RaiseLocalEvent(mark, new InteractUsingEvent(user, steel, mark, markCoords));
        });

        await server.WaitRunTicks(180);

        await server.WaitAssertion(() =>
        {
            var found = false;
            var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "Girder")
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "the chalk mark should construct the planned girder");
        });

        await pair.CleanReturnAsync();
    }
}
