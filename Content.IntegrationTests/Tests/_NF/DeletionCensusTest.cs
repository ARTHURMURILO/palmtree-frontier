using System.Numerics;
using Content.Server._NF.GC.Components;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Prototypes;
using Content.Shared._NF.CCVar;
using Content.Shared._NF.Item;
using Content.Shared._NF.Pinpointer;
using Content.Shared.Cargo.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mind;
using Robust.Server.GameObjects;
using Robust.Server.Physics;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._NF;

/// <summary>
/// End-to-end tests for the deletion census (garbage collection):
/// culling of abandoned entities, chunk-loading gating, exemption components,
/// pinpointer/value/player-touched protection, loot rescue from culled grids
/// and the station exemption.
/// </summary>
[TestFixture]
public sealed class DeletionCensusTest
{
    [TestPrototypes]
    private const string TestPrototypes = @"
- type: worldgenConfig
  id: GCWorldgenTest
  components:
    - type: WorldController
";

    /// <summary>
    /// Test-only system used to raise events normally raised by interaction code,
    /// and to run queries that are not exposed on <see cref="IEntityManager"/>.
    /// </summary>
    public sealed class DeletionCensusTestSystem : EntitySystem
    {
        public void RaisePickedUp(EntityUid user, EntityUid item)
        {
            RaiseLocalEvent(item, new PickedUpEvent(user, item), false);
        }

        public void RaiseDropped(EntityUid user, EntityUid item)
        {
            RaiseLocalEvent(item, new DroppedEvent(user), false);
        }

        public void RaiseGridSplit(EntityUid grid, params EntityUid[] newGrids)
        {
            var ev = new GridSplitEvent(newGrids, grid);
            RaiseLocalEvent(grid, ref ev, false);
        }

        public EntityUid FindStationGridOnMap(EntityUid mapUid)
        {
            var query = AllEntityQuery<BecomesStationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == mapUid)
                    return uid;
            }

            return EntityUid.Invalid;
        }
    }

    // Spots used by the test, well away from the station and from each other.
    private static readonly Vector2 SpaceA = new(1000f, 1000f);
    private static readonly Vector2 SpaceB = new(-1000f, 1000f);
    private static readonly Vector2 SpaceC = new(2000f, -1000f);
    private static readonly Vector2 SpaceD = new(-2000f, -2000f);
    private static readonly Vector2 FarAway = new(5000f, 5000f);
    private static readonly Vector2 GridSpotA = new(3000f, 3000f);
    private static readonly Vector2 GridSpotB = new(-3000f, 3000f);

    [Test]
    public async Task CullsAbandonedEntitiesAndRespectsProtections()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Dirty = true,
        });

        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var serMan = server.ResolveDependency<ISerializationManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var ticker = server.System<GameTicker>();
        var xform = server.System<TransformSystem>();
        var mind = server.System<SharedMindSystem>();
        var testSys = server.System<DeletionCensusTestSystem>();

        var mapUid = EntityUid.Invalid;

        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var mapSys = server.System<SharedMapSystem>();

        // Setup: fast census passes, protections on their defaults unless under test.
        await server.WaitPost(() =>
        {
            cfg.SetCVar(NFCCVars.GarbageCollectionEnabled, true);
            cfg.SetCVar(NFCCVars.GarbageCollectionPeriod, 1); // A census pass every second.
            cfg.SetCVar(NFCCVars.GarbageCollectionEntitiesPerTick, 100_000); // One pass = one tick.
            cfg.SetCVar(NFCCVars.GarbageCollectionTally, 1); // One idle pass is enough to cull.
            cfg.SetCVar(NFCCVars.GarbageCollectionMaxValue, 0); // No value protection unless under test.
            cfg.SetCVar(NFCCVars.GarbageCollectionGridMaxValue, 0);
            cfg.SetCVar(NFCCVars.GarbageCollectionTouchedImmune, true);
            cfg.SetCVar(NFCCVars.GarbageCollectionTouchedDuration, 60);
            cfg.SetCVar(NFCCVars.GarbageCollectionIgnoreLoadedChunks, false);

            Assert.That(mapSys.TryGetMap(ticker.DefaultMap, out var defaultMapUid), Is.True,
                "Test round did not load a default map.");
            mapUid = defaultMapUid!.Value;
        });

        var worldgenConfig = protoMan.Index<WorldgenConfigPrototype>("GCWorldgenTest");

        // Loaded chunks protect entities: this is why a dropped item survives next to a player.
        EntityUid gatedLoader = default;
        EntityUid gatedItem = default;
        EntityUid canaryA = default;

        await server.WaitPost(() =>
        {
            // The test harness disables worldgen, so install a bare world controller by hand.
            worldgenConfig.Apply(mapUid, serMan, entMan);

            gatedLoader = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceA));
            entMan.EnsureComponent<WorldLoaderComponent>(gatedLoader);
        });

        await server.WaitRunTicks(10); // Let the world controller create and load the chunk.

        await server.WaitPost(() =>
        {
            gatedItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceA));
            canaryA = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceD));
        });

        await WaitUntilCulled(server, entMan, canaryA, "unloaded canary");
        await server.WaitPost(() => Assert.That(
            entMan.EntityExists(gatedItem),
            Is.True,
            "An entity on a loaded chunk (world loader nearby) should not be culled."));

        // Removing the loader unloads the chunk, and then the census culls the item.
        await server.WaitPost(() => entMan.DeleteEntity(gatedLoader));
        await WaitUntilCulled(server, entMan, gatedItem, "item on a chunk that unloaded");

        // nf14.gc.ignore_loaded_chunks bypasses the loaded-chunk gate.
        EntityUid bypassLoader = default;
        EntityUid bypassItem = default;

        await server.WaitPost(() =>
        {
            bypassLoader = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceB));
            entMan.EnsureComponent<WorldLoaderComponent>(bypassLoader);
        });

        await server.WaitRunTicks(10);

        await server.WaitPost(() =>
        {
            bypassItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceB));
            cfg.SetCVar(NFCCVars.GarbageCollectionIgnoreLoadedChunks, true);
        });

        await WaitUntilCulled(server, entMan, bypassItem, "item on a loaded chunk with the debug CVar on");

        // Exemptions: component, pinpointer tracking and the value guard.
        EntityUid exemptItem = default;
        EntityUid pinnedItem = default;
        EntityUid valuableItem = default;
        EntityUid canaryB = default;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(NFCCVars.GarbageCollectionMaxValue, 3000);

            exemptItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC));
            entMan.EnsureComponent<DeletionCensusExemptComponent>(exemptItem);

            pinnedItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC + new Vector2(16f, 0f)));
            entMan.EnsureComponent<PinpointerTargetComponent>(pinnedItem);

            valuableItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC + new Vector2(32f, 0f)));
            entMan.EnsureComponent<StaticPriceComponent>(valuableItem).Price = 5000;

            canaryB = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC + new Vector2(48f, 0f)));
        });

        await WaitUntilCulled(server, entMan, canaryB, "unprotected canary");
        await server.WaitPost(() => Assert.Multiple(() =>
        {
            Assert.That(entMan.EntityExists(exemptItem), Is.True, "DeletionCensusExempt entities should not be culled.");
            Assert.That(entMan.EntityExists(pinnedItem), Is.True, "Pinpointer-tracked entities should not be culled.");
            Assert.That(entMan.EntityExists(valuableItem), Is.True, "Entities over nf14.gc.max_value should not be culled.");
        }));

        // Removing pinpointer tracking makes the item cullable.
        await server.WaitPost(() => entMan.RemoveComponent<PinpointerTargetComponent>(pinnedItem));
        await WaitUntilCulled(server, entMan, pinnedItem, "item after pinpointer tracking was removed");

        // Disabling the value guard makes the valuable item cullable.
        await server.WaitPost(() => cfg.SetCVar(NFCCVars.GarbageCollectionMaxValue, 0));
        await WaitUntilCulled(server, entMan, valuableItem, "valuable item with the value guard disabled");

        // The exempt entity survives everything.
        await server.WaitPost(() => Assert.That(
            entMan.EntityExists(exemptItem),
            Is.True,
            "DeletionCensusExempt entities should survive any number of passes."));

        // Player-touched protection, and the pickup/drop events that maintain it.
        EntityUid dummyUser = default;
        EntityUid touchedItem = default;
        EntityUid canaryC = default;

        await server.WaitPost(() =>
        {
            dummyUser = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceD));
            touchedItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC + new Vector2(64f, 0f)));
            var touched = entMan.EnsureComponent<PlayerTouchedComponent>(touchedItem);
            touched.ProtectionExpiry = timing.CurTime + TimeSpan.FromMinutes(5);

            canaryC = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC + new Vector2(80f, 0f)));
        });

        await WaitUntilCulled(server, entMan, canaryC, "unprotected canary");
        await server.WaitPost(() => Assert.That(
            entMan.EntityExists(touchedItem),
            Is.True,
            "Player-touched entities should be protected until their expiry."));

        // An expired protection no longer saves the item.
        await server.WaitPost(() => entMan.GetComponent<PlayerTouchedComponent>(touchedItem).ProtectionExpiry =
            timing.CurTime - TimeSpan.FromSeconds(1));
        await WaitUntilCulled(server, entMan, touchedItem, "player-touched item with an expired protection");

        // Real items are marked when picked up, and get an expiry when dropped.
        EntityUid pickupItem = default;
        await server.WaitPost(() =>
        {
            // Bare entities have no ItemComponent, and the marker is only applied to items.
            pickupItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC + new Vector2(96f, 0f)));
            entMan.EnsureComponent<ItemComponent>(pickupItem);
            cfg.SetCVar(NFCCVars.GarbageCollectionTouchedDuration, 7);
            testSys.RaisePickedUp(dummyUser, pickupItem);

            Assert.That(entMan.TryGetComponent<PlayerTouchedComponent>(pickupItem, out var touched), Is.True,
                "Picking an item up should mark it as player-touched.");
            Assert.That(touched!.ProtectionExpiry, Is.Null, "A held item should have no protection expiry.");
        });

        await server.WaitPost(() =>
        {
            testSys.RaiseDropped(dummyUser, pickupItem);

            var touched = entMan.GetComponent<PlayerTouchedComponent>(pickupItem);
            Assert.That(touched.ProtectionExpiry, Is.Not.Null, "Dropping an item should start its protection expiry.");
            var remaining = (touched.ProtectionExpiry!.Value - timing.CurTime).TotalSeconds;
            Assert.That(remaining, Is.InRange(5.0, 9.0),
                $"Dropped item protection should last ~7 seconds (nf14.gc.touched_duration), got {remaining:F1}s.");
        });

        // A short touched duration means a dropped item's protection lapses and it is culled.
        await server.WaitPost(() =>
        {
            cfg.SetCVar(NFCCVars.GarbageCollectionTouchedDuration, 1);
            testSys.RaiseDropped(dummyUser, pickupItem);
        });
        await WaitUntilCulled(server, entMan, pickupItem, "dropped item after its protection lapsed");

        // A duration of 0 keeps protection forever.
        EntityUid permanentItem = default;
        EntityUid canaryD = default;
        await server.WaitPost(() =>
        {
            permanentItem = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceC + new Vector2(112f, 0f)));
            entMan.EnsureComponent<ItemComponent>(permanentItem);
            cfg.SetCVar(NFCCVars.GarbageCollectionTouchedDuration, 0);
            testSys.RaisePickedUp(dummyUser, permanentItem);
            testSys.RaiseDropped(dummyUser, permanentItem);

            Assert.That(entMan.GetComponent<PlayerTouchedComponent>(permanentItem).ProtectionExpiry, Is.Null,
                "A touched_duration of 0 should keep protection forever.");

            canaryD = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceD + new Vector2(16f, 0f)));
        });

        await WaitUntilCulled(server, entMan, canaryD, "unprotected canary");
        await server.WaitPost(() => Assert.That(
            entMan.EntityExists(permanentItem),
            Is.True,
            "Player-touched items with touched_duration 0 should be protected indefinitely."));

        // Loot rescue: culling a grid reparents protected loot instead of deleting it with the grid.
        EntityUid cullGrid = default;
        EntityUid doomedItem = default;
        EntityUid rescuedTouched = default;
        EntityUid rescuedPinned = default;
        await server.WaitPost(() =>
        {
            cfg.SetCVar(NFCCVars.GarbageCollectionGridMaxValue, 30000);
            cfg.SetCVar(NFCCVars.GarbageCollectionTouchedDuration, 60);

            var gridEnt = mapMan.CreateGridEntity(ticker.DefaultMap);
            cullGrid = gridEnt.Owner;
            xform.SetCoordinates(cullGrid, new EntityCoordinates(mapUid, GridSpotA));

            // Spawned entities only attach to a grid that actually has tiles at the spawn point.
            var plating = tileDefs["Plating"];
            mapSys.SetTile(cullGrid, gridEnt.Comp, new EntityCoordinates(cullGrid, 0, 0), new Tile(plating.TileId));
            var lootSpot = new EntityCoordinates(cullGrid, 0.5f, 0.5f);

            doomedItem = entMan.SpawnEntity(null, lootSpot);

            rescuedTouched = entMan.SpawnEntity(null, lootSpot);
            entMan.EnsureComponent<PlayerTouchedComponent>(rescuedTouched).ProtectionExpiry =
                timing.CurTime + TimeSpan.FromMinutes(5);

            rescuedPinned = entMan.SpawnEntity(null, lootSpot);
            entMan.EnsureComponent<PinpointerTargetComponent>(rescuedPinned);

            // Sanity: the loot really is on the grid, or the rescue test is meaningless.
            Assert.That(entMan.GetComponent<TransformComponent>(doomedItem).GridUid, Is.EqualTo(cullGrid),
                "Test setup error: loot should be parented to the test grid.");
        });

        await WaitUntilCulled(server, entMan, doomedItem, "untouched item on a culled grid");
        await server.WaitPost(() => Assert.Multiple(() =>
        {
            Assert.That(entMan.EntityExists(cullGrid), Is.False, "An unprotected grid should be culled by the census.");
            Assert.That(entMan.EntityExists(rescuedTouched), Is.True, "Player-touched loot should be rescued from a culled grid.");
            Assert.That(entMan.EntityExists(rescuedPinned), Is.True, "Pinpointer-tracked loot should be rescued from a culled grid.");
            Assert.That(entMan.GetComponent<TransformComponent>(rescuedTouched).GridUid, Is.Null, "Rescued loot should be parented to the map.");
            Assert.That(entMan.GetComponent<TransformComponent>(rescuedPinned).GridUid, Is.Null, "Rescued loot should be parented to the map.");
        }));

        // Grids worth more than nf14.gc.grid_max_value are left alone.
        EntityUid valuableGrid = default;
        EntityUid valuableLoot = default;
        EntityUid canaryE = default;
        var pricing = server.System<PricingSystem>();
        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(ticker.DefaultMap);
            valuableGrid = gridEnt.Owner;
            xform.SetCoordinates(valuableGrid, new EntityCoordinates(mapUid, GridSpotB));

            var plating = tileDefs["Plating"];
            mapSys.SetTile(valuableGrid, gridEnt.Comp, new EntityCoordinates(valuableGrid, 0, 0), new Tile(plating.TileId));

            valuableLoot = entMan.SpawnEntity(null, new EntityCoordinates(valuableGrid, 0.5f, 0.5f));
            entMan.EnsureComponent<StaticPriceComponent>(valuableLoot).Price = 50_000;

            canaryE = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceD + new Vector2(32f, 0f)));

            // Sanity checks: the setup must be what the value guard cares about before we assert on the census.
            Assert.That(entMan.GetComponent<TransformComponent>(valuableGrid).ParentUid, Is.EqualTo(mapUid),
                "Test grid should be parented to the default map.");
            Assert.That(entMan.GetComponent<TransformComponent>(valuableLoot).GridUid, Is.EqualTo(valuableGrid),
                "Valuable loot should be on the test grid.");
            Assert.That(cfg.GetCVar(NFCCVars.GarbageCollectionGridMaxValue), Is.EqualTo(30000),
                "grid_max_value CVar should be 30000.");
            var gridPrice = pricing.AppraiseGrid(valuableGrid);
            Assert.That(gridPrice, Is.GreaterThan(30000),
                $"Test grid should appraise above the grid value threshold, appraised: {gridPrice}");
        });

        await WaitUntilCulled(server, entMan, canaryE, "unprotected canary");
        await server.WaitPost(() => Assert.That(
            entMan.EntityExists(valuableGrid),
            Is.True,
            "A grid worth more than nf14.gc.grid_max_value should not be culled."));

        await server.WaitPost(() => cfg.SetCVar(NFCCVars.GarbageCollectionGridMaxValue, 0));
        await WaitUntilCulled(server, entMan, valuableGrid, "valuable grid with the grid value guard disabled");

        // The station grid is exempt, and its exemption passes on to split grids.
        var stationGrid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            stationGrid = testSys.FindStationGridOnMap(mapUid);

            Assert.That(stationGrid, Is.Not.EqualTo(EntityUid.Invalid), "No BecomesStation grid found on the default map.");
            Assert.That(entMan.HasComponent<DeletionCensusExemptComponent>(stationGrid), Is.True,
                "Station grids should be exempt from the census.");
            Assert.That(entMan.GetComponent<DeletionCensusExemptComponent>(stationGrid).PassOnGridSplit, Is.True,
                "Station exemptions should pass on to split grids.");
        });

        EntityUid canaryF = default;
        await server.WaitPost(() => canaryF =
            entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceD + new Vector2(48f, 0f))));
        await WaitUntilCulled(server, entMan, canaryF, "unprotected canary");
        await server.WaitPost(() => Assert.That(
            entMan.EntityExists(stationGrid),
            Is.True,
            "Station grids should never be culled."));

        await server.WaitPost(() =>
        {
            var splitGrid = mapMan.CreateGridEntity(ticker.DefaultMap).Owner;
            testSys.RaiseGridSplit(stationGrid, splitGrid);

            Assert.That(entMan.HasComponent<DeletionCensusExemptComponent>(splitGrid), Is.True,
                "Splitting an exempt grid should pass the exemption on to the new grid.");
        });

        // Entities with a mind are never culled.
        EntityUid mob = default;
        EntityUid canaryG = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(mapUid, FarAway));
            var mindEnt = mind.CreateMind(null);
            mind.TransferTo(mindEnt.Owner, mob);

            canaryG = entMan.SpawnEntity(null, new EntityCoordinates(mapUid, SpaceD + new Vector2(64f, 0f)));
        });

        await WaitUntilCulled(server, entMan, canaryG, "unprotected canary");
        await server.WaitPost(() => Assert.That(
            entMan.EntityExists(mob),
            Is.True,
            "Entities with a mind should not be culled by the census."));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Runs the server until the given entity is deleted by the census, failing the test on timeout.
    /// </summary>
    private static async Task WaitUntilCulled(
        RobustIntegrationTest.ServerIntegrationInstance server,
        IEntityManager entMan,
        EntityUid uid,
        string what,
        int maxTicks = 1200)
    {
        for (var i = 0; i < maxTicks / 10; i++)
        {
            var gone = false;
            await server.WaitPost(() => gone = !entMan.EntityExists(uid));
            if (gone)
                return;

            await server.WaitRunTicks(10);
        }

        Assert.Fail($"'{what}' ({uid}) was not culled by the census within {maxTicks} ticks.");
    }
}
