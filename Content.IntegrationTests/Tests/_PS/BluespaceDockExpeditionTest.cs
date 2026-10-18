using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server._NF.Station.Components;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Station;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: a retrieved ship must come back expedition capable. "Expedition capable" is a property of
/// the ship's *station*: the salvage expedition console reads its mission list from the
/// <see cref="SalvageExpeditionDataComponent"/> of the station its grid belongs to, and only the
/// expedition vessel station prototype (<c>StandardFrontierExpeditionVessel</c>) has that component.
/// The retrieve path used to rebuild every ship with a plain <c>StandardFrontierVessel</c> station, so
/// expedition consoles came back with nothing to show and every button disabled.
/// </summary>
[TestFixture]
public sealed class BluespaceDockExpeditionTest
{
    /// <summary>A civilian expedition vessel from the shipyard's Expedition group.</summary>
    private const string ExpeditionVessel = "Aegis";

    private const string ConsoleProto = "ComputerTabletopSalvageExpedition";

    [Test]
    public async Task RetrievedExpeditionShipComesBackExpeditionCapable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();

        var session = playerMan.Sessions.Single();
        var userId = session.UserId;

        EntityUid playerMob = default;
        EntityUid console = default;
        EntityUid shipGrid = default;
        var shipId = string.Empty;
        var characterName = string.Empty;

        await server.WaitPost(() =>
        {
            playerMob = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.AddComponent<Content.Shared._NF.Bank.Components.BankAccountComponent>(playerMob);
            playerMan.SetAttachedEntity(session, playerMob);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var sharedDock = server.System<SharedBluespaceDockSystem>();
            var itemSlots = server.System<ItemSlotsSystem>();
            var mapSystem = server.System<SharedMapSystem>();
            var stationSystem = server.System<StationSystem>();
            var xformSystem = server.System<SharedTransformSystem>();
            dock.ReloadRegistry();

            // Station grid with the dock console (the station the ship is stored from and docked to).
            var stationGrid = testMap.Grid.Owner;
            mapSystem.SetTiles(stationGrid, entMan.GetComponent<MapGridComponent>(stationGrid),
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)) });

            stationSystem.InitializeNewStation(
                new StationConfig { StationPrototype = "StandardFrontierVessel" },
                new List<EntityUid> { stationGrid },
                "Diag Station");

            var serviceHost = entMan.SpawnEntity(null, new EntityCoordinates(stationGrid, Vector2.Zero));
            entMan.AddComponent<Content.Server._NF.SectorServices.StationSectorServiceHostComponent>(serviceHost);

            console = entMan.SpawnEntity("ComputerBluespaceDock",
                new EntityCoordinates(stationGrid, new Vector2(0.5f, 0.5f)));

            var idCard = entMan.SpawnEntity("PassengerIDCard",
                new EntityCoordinates(stationGrid, new Vector2(0.5f, 0.5f)));
            var consoleComp = entMan.GetComponent<BluespaceDockConsoleComponent>(console);
            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, idCard, null), Is.True,
                "Could not insert the ID card.");

            // Ship grid with a salvage expedition console on it.
            shipGrid = mapMan.CreateGridEntity(testMap.MapId).Owner;
            xformSystem.SetLocalPosition(shipGrid, new Vector2(50f, 0f));
            mapSystem.SetTiles(shipGrid, entMan.GetComponent<MapGridComponent>(shipGrid),
                new List<(Vector2i, Tile)>
                {
                    (new Vector2i(0, 0), new Tile(1)),
                    (new Vector2i(1, 0), new Tile(1)),
                });

            entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(shipGrid, new Vector2(0.5f, 0.5f)));
            entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(shipGrid, new Vector2(1.5f, 0.5f)));

            // Give the ship the vessel identity a shipyard purchase gives it: the station from the
            // vessel's game map prototype (StandardFrontierExpeditionVessel for an expedition ship)
            // and the vessel id on it. This is what makes the console work in the first place.
            Assert.That(prototypes.TryIndex<GameMapPrototype>(ExpeditionVessel, out var map), Is.True,
                $"The test vessel {ExpeditionVessel} has no game map prototype.");
            var shipStation = stationSystem.InitializeNewStation(map.Stations[ExpeditionVessel], new List<EntityUid> { shipGrid });
            entMan.GetComponent<ExtraShuttleInformationComponent>(shipStation).Vessel = ExpeditionVessel;

            // Sanity: before storing, the console's station carries the expedition data.
            Assert.That(entMan.HasComponent<SalvageExpeditionDataComponent>(shipStation), Is.True,
                "sanity: an expedition vessel's station must start with expedition data");
            Assert.That(stationSystem.GetOwningStation(FindConsole(entMan, shipGrid)), Is.EqualTo(shipStation),
                "sanity: the console must belong to the ship's station");

            sharedDock.AssignDeed(shipGrid, shipGrid, "Exped Ship", null, "Exped Owner");

            var idName = entMan.GetComponent<Content.Shared.Access.Components.IdCardComponent>(idCard).FullName;
            characterName = dock.GetCharacterName(userId, idName, playerMob);
            Assert.That(characterName.Length, Is.GreaterThan(0), "No character name resolved.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();

            Assert.That(
                dock.TryStoreShip(shipGrid, userId.ToString(), characterName, 100, payer: null, cost: 0,
                    out var storeError),
                Is.True, storeError);

            var entry = dock.FindEntry(userId.ToString(), characterName, "Exped Ship");
            Assert.That(entry, Is.Not.Null, "No registry entry after the store.");
            Assert.That(entry!.Vessel, Is.EqualTo(ExpeditionVessel),
                "The stored ship lost its vessel identity.");
            shipId = entry.ShipId;
        });

        await server.WaitAssertion(() =>
        {
            // Drive the real retrieve message exactly like the console UI does.
            var msg = new BluespaceDockRetrieveMessage(shipId) { Actor = playerMob };
            entMan.EventBus.RaiseLocalEvent(console, msg);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var stationSystem = server.System<StationSystem>();

            EntityUid? retrieved = null;
            foreach (var deedShip in dock.GetDeedShips())
            {
                if (deedShip.Name.StartsWith("Exped Ship"))
                    retrieved = deedShip.Grid;
            }

            Assert.That(retrieved, Is.Not.Null, "The retrieved ship has no deed; retrieval failed.");

            var station = stationSystem.GetOwningStation(retrieved!.Value);
            Assert.That(station, Is.Not.Null, "The retrieved ship has no station.");

            Assert.That(entMan.HasComponent<SalvageExpeditionDataComponent>(station!.Value), Is.True,
                "The retrieved ship's station lost its expedition data: its expedition consoles are " +
                "dead and the ship can no longer run expeditions.");

            Assert.That(
                entMan.GetComponent<ExtraShuttleInformationComponent>(station.Value).Vessel,
                Is.EqualTo(ExpeditionVessel), "The retrieved ship lost its vessel identity.");

            var retrievedConsole = FindConsole(entMan, retrieved.Value);
            Assert.That(retrievedConsole, Is.Not.EqualTo(default(EntityUid)),
                "The retrieved ship lost its expedition console.");
            Assert.That(stationSystem.GetOwningStation(retrievedConsole), Is.EqualTo(station.Value),
                "The retrieved ship's expedition console is not on the ship's station any more.");
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid FindConsole(IEntityManager entMan, EntityUid grid)
    {
        var metaQuery = entMan.GetEntityQuery<MetaDataComponent>();
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (metaQuery.TryGetComponent(child, out var meta) && meta.EntityPrototype?.ID == ConsoleProto)
                return child;
        }

        return default;
    }
}
