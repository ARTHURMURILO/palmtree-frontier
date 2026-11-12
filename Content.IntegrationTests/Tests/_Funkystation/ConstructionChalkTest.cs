// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Funkystation.ConstructionChalk;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Guards the construction chalk flow: a placed mark remembers its recipe, and using a
///     construction material on it starts the normal construction do-after at the mark's tile.
/// </summary>
[TestFixture]
public sealed class ConstructionChalkTest
{
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
