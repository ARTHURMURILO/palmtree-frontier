using System.Collections.Generic;
using System.Numerics;
using Content.Server.Tabletop;
using Content.Server.Tabletop.Components;
using Content.Shared.CCVar;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Tabletop;
using Content.Shared.Tabletop.Components;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: items placed on a battlemap must become sanitized tabletop pieces. Previously the
/// interaction copied the item's prototype into the tabletop map, which let arbitrary components
/// (and unbounded entity spam) reach a map meant only for game pieces.
/// </summary>
[TestFixture]
public sealed class TabletopPlacementTest
{
    private const string BattlemapPrototype = "MoonBattlemap";
    private const string FigurinePrototype = "ToyFigurineWizard";
    private const string HologramPrototype = "PalmtreeTabletopHologram";

    [Test]
    public async Task FigurineBecomesSanitizedTabletopPiece()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(CCVars.GameTabletopPlace, true);

            var coordinates = new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f));
            var board = entMan.SpawnEntity(BattlemapPrototype, coordinates);
            var figurine = entMan.SpawnEntity(FigurinePrototype, coordinates);
            var user = entMan.SpawnEntity("MobHuman", coordinates);

            var hands = server.System<SharedHandsSystem>();
            Assert.That(hands.TryForcePickupAnyHand(user, figurine), Is.True,
                "sanity: the user must hold the figurine");

            var tabletop = entMan.GetComponent<TabletopGameComponent>(board);
            var sessions = server.System<TabletopSystem>();
            var session = sessions.EnsureSession(tabletop);

            var interact = new InteractUsingEvent(user, figurine, board,
                entMan.GetComponent<TransformComponent>(board).Coordinates);
            entMan.EventBus.RaiseLocalEvent(board, interact);

            EntityUid? piece = null;
            foreach (var sessionEntity in session.Entities)
            {
                if (entMan.HasComponent<TabletopHologramComponent>(sessionEntity))
                    piece = sessionEntity;
            }

            Assert.That(piece, Is.Not.Null, "Placing the figurine did not create a tabletop piece.");

            var meta = entMan.GetComponent<MetaDataComponent>(piece!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(meta.EntityPrototype?.ID, Is.EqualTo(HologramPrototype),
                    "Placed pieces must be sanitized dummy entities, not copies of the source item.");
                Assert.That(entMan.HasComponent<ItemComponent>(piece.Value), Is.False,
                    "The dummy piece must not inherit item components from the source item.");
                Assert.That(entMan.HasComponent<TabletopDraggableComponent>(piece.Value), Is.True,
                    "The placed piece must be draggable.");
            });

            var appearance = server.System<SharedAppearanceSystem>();
            Assert.That(
                appearance.TryGetData<string>(piece.Value, TabletopItemVisuals.Prototype, out var protoId),
                Is.True, "The placed piece has no source prototype recorded for its sprite.");
            Assert.That(protoId, Is.EqualTo(FigurinePrototype));

            // Fill the board up to the cap and make sure one more does not create an entity.
            for (var i = 1; i < 50; i++)
            {
                var extra = new InteractUsingEvent(user, figurine, board,
                    entMan.GetComponent<TransformComponent>(board).Coordinates);
                entMan.EventBus.RaiseLocalEvent(board, extra);
            }

            Assert.That(CountHolograms(entMan, session.Entities), Is.EqualTo(50),
                "The board did not reach its placement cap.");

            var overLimit = new InteractUsingEvent(user, figurine, board,
                entMan.GetComponent<TransformComponent>(board).Coordinates);
            entMan.EventBus.RaiseLocalEvent(board, overLimit);

            Assert.That(CountHolograms(entMan, session.Entities), Is.EqualTo(50),
                "The board must not accept pieces beyond its placement cap.");
        });

        await pair.CleanReturnAsync();
    }

    private static int CountHolograms(IEntityManager entMan, IEnumerable<EntityUid> entities)
    {
        var count = 0;
        foreach (var entity in entities)
        {
            if (entMan.HasComponent<TabletopHologramComponent>(entity))
                count++;
        }

        return count;
    }
}
