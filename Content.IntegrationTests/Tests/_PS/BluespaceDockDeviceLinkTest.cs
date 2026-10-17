using System.Collections.Generic;
using System.Linq;
using Content.Server._PS.BluespaceDock;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Station;
using Content.Shared.Storage;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: a ship stored in the bluespace dock and retrieved again must come back with its device
/// network registrations (buttons and logic keep working) and with exactly the container contents it
/// was saved with (no re-run of prototype fills).
/// </summary>
[TestFixture]
public sealed class BluespaceDockDeviceLinkTest
{
    [Test]
    public async Task RetrievedShipKeepsDeviceLinksAndContainerContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var testMap = await pair.CreateTestMap();

        var session = playerMan.Sessions.Single();
        var userId = session.UserId;

        EntityUid playerMob = default;
        EntityUid console = default;
        EntityUid button = default;
        EntityUid shutter = default;
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
            var stationSystem = server.System<StationSystem>();
            var deviceLink = server.System<DeviceLinkSystem>();
            var mapSystem = server.System<SharedMapSystem>();
            var itemSlots = server.System<ItemSlotsSystem>();
            dock.ReloadRegistry();

            // Station grid with the dock console.
            var stationGrid = testMap.Grid.Owner;
            mapSystem.SetTiles(stationGrid, entMan.GetComponent<MapGridComponent>(stationGrid),
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)) });

            stationSystem.InitializeNewStation(
                new StationConfig { StationPrototype = "StandardFrontierVessel" },
                new List<EntityUid> { stationGrid },
                "Diag Station");

            // The shuttle records store lives on the sector service entity, which a station map
            // creates through its sector service host component.
            var serviceHost = entMan.SpawnEntity(null, new EntityCoordinates(stationGrid, System.Numerics.Vector2.Zero));
            entMan.AddComponent<Content.Server._NF.SectorServices.StationSectorServiceHostComponent>(serviceHost);

            console = entMan.SpawnEntity("ComputerBluespaceDock",
                new EntityCoordinates(stationGrid, new System.Numerics.Vector2(0.5f, 0.5f)));

            var idCard = entMan.SpawnEntity("PassengerIDCard",
                new EntityCoordinates(stationGrid, new System.Numerics.Vector2(0.5f, 0.5f)));
            var consoleComp = entMan.GetComponent<BluespaceDockConsoleComponent>(console);
            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, idCard, null), Is.True,
                "Could not insert the ID card.");

            // Ship grid with a linked button and shutter (the shutter gets its own tile so nothing
            // blocks the door's closing safety check).
            var shipGrid = mapMan.CreateGridEntity(testMap.MapId).Owner;
            mapSystem.SetTiles(shipGrid, entMan.GetComponent<MapGridComponent>(shipGrid),
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)), (new Vector2i(1, 0), new Tile(1)) });

            button = entMan.SpawnEntity("SignalButton",
                new EntityCoordinates(shipGrid, new System.Numerics.Vector2(0.5f, 0.5f)));
            shutter = entMan.SpawnEntity("ShuttersNormalOpen",
                new EntityCoordinates(shipGrid, new System.Numerics.Vector2(1.5f, 0.5f)));
            deviceLink.SaveLinks(null, button, shutter, new List<(string, string)> { ("Pressed", "Toggle") });
            Assert.That(deviceLink.GetLinks(button, shutter).Count, Is.EqualTo(1), "Test link was not created.");

            // The shutter starts open; close it with the button. Device network packets are
            // processed on the next tick, so the state is checked after ticks are run.
            Assert.That(entMan.GetComponent<DoorComponent>(shutter).State, Is.EqualTo(DoorState.Open));
            deviceLink.InvokePort(button, "Pressed");

            sharedDock.AssignDeed(shipGrid, shipGrid, "Diag Ship", null, "Diag Owner");

            var idName = entMan.GetComponent<IdCardComponent>(idCard).FullName;
            characterName = dock.GetCharacterName(userId, idName, playerMob);
            Assert.That(characterName.Length, Is.GreaterThan(0), "No character name resolved.");
        });

        // Let the button's signal arrive and the shutter finish closing.
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(shutter).State, Is.EqualTo(DoorState.Closed),
                "The device link does not work before storing.");
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var sharedDock = server.System<SharedBluespaceDockSystem>();
            var storage = server.System<StorageSystem>();
            var mapSystem = server.System<SharedMapSystem>();

            // A filled toolbox the "player" emptied and refilled with a single rubber duck: storing
            // and retrieving must not resurrect the prototype's fill contents.
            var shipGrid = entMan.GetComponent<TransformComponent>(button).GridUid!.Value;
            var toolbox = entMan.SpawnEntity("ToolboxMechanicalFilled",
                new EntityCoordinates(shipGrid, new System.Numerics.Vector2(0.4f, 0.4f)));
            var toolStorage = entMan.GetComponent<StorageComponent>(toolbox);
            Assert.That(toolStorage.Container.ContainedEntities.Count, Is.GreaterThan(0),
                "The toolbox should have been filled by StorageFill on spawn.");
            foreach (var item in toolStorage.Container.ContainedEntities.ToList())
                entMan.DeleteEntity(item);

            var duck = entMan.SpawnEntity("ToyRubberDuck",
                new EntityCoordinates(shipGrid, new System.Numerics.Vector2(0.4f, 0.4f)));
            Assert.That(storage.Insert(toolbox, duck, out _, out var reason), Is.True,
                $"Could not insert the duck: {reason}");

            Assert.That(
                dock.TryStoreShip(shipGrid, userId.ToString(), characterName, 100, payer: null, cost: 0,
                    out var storeError),
                Is.True, storeError);

            var entry = dock.FindEntry(userId.ToString(), characterName, "Diag Ship");
            Assert.That(entry, Is.Not.Null, "No registry entry after the store.");
            shipId = entry!.ShipId;
        });

        await server.WaitAssertion(() =>
        {
            // Drive the real retrieve message exactly like the console UI does.
            var msg = new BluespaceDockRetrieveMessage(shipId) { Actor = playerMob };
            entMan.EventBus.RaiseLocalEvent(console, msg);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var deviceLink = server.System<DeviceLinkSystem>();
            var deviceNetwork = server.System<DeviceNetworkSystem>();

            EntityUid? ship = null;
            foreach (var deedShip in dock.GetDeedShips())
            {
                if (deedShip.Name.StartsWith("Diag Ship"))
                    ship = deedShip.Grid;
            }

            Assert.That(ship, Is.Not.Null, "The retrieved ship has no deed; retrieval failed.");

            EntityUid? foundButton = null;
            EntityUid? foundShutter = null;
            EntityUid? foundToolbox = null;
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(ship!.Value, entities);
            foreach (var uid in entities)
            {
                var proto = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (proto == "SignalButton")
                    foundButton = uid;
                else if (proto == "ShuttersNormalOpen")
                    foundShutter = uid;
                else if (proto == "ToolboxMechanicalFilled")
                    foundToolbox = uid;
            }

            Assert.That(foundButton, Is.Not.Null, "The retrieved ship has no button.");
            Assert.That(foundShutter, Is.Not.Null, "The retrieved ship has no shutter.");
            Assert.That(foundToolbox, Is.Not.Null, "The retrieved ship has no toolbox.");

            // Device network registration must have been restored for both devices.
            Assert.That(entMan.TryGetComponent(foundButton!.Value, out DeviceNetworkComponent? buttonNet), Is.True);
            Assert.That(entMan.TryGetComponent(foundShutter!.Value, out DeviceNetworkComponent? shutterNet), Is.True);
            Assert.That(deviceNetwork.IsDeviceConnected(foundButton.Value, buttonNet), Is.True,
                "The retrieved button is not connected to the device network.");
            Assert.That(deviceNetwork.IsDeviceConnected(foundShutter.Value, shutterNet), Is.True,
                "The retrieved shutter is not connected to the device network.");

            // And the button must actually drive the shutter again (the packet arrives next tick).
            Assert.That(entMan.GetComponent<DoorComponent>(foundShutter.Value).State, Is.EqualTo(DoorState.Closed),
                "The shutter should have come back closed.");
            deviceLink.InvokePort(foundButton.Value, "Pressed");
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();

            EntityUid? ship = null;
            foreach (var deedShip in dock.GetDeedShips())
            {
                if (deedShip.Name.StartsWith("Diag Ship"))
                    ship = deedShip.Grid;
            }

            Assert.That(ship, Is.Not.Null);

            EntityUid? foundShutter = null;
            EntityUid? foundToolbox = null;
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(ship!.Value, entities);
            foreach (var uid in entities)
            {
                var proto = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (proto == "ShuttersNormalOpen")
                    foundShutter = uid;
                else if (proto == "ToolboxMechanicalFilled")
                    foundToolbox = uid;
            }

            Assert.That(foundShutter, Is.Not.Null);
            Assert.That(foundToolbox, Is.Not.Null);

            Assert.That(entMan.GetComponent<DoorComponent>(foundShutter!.Value).State,
                Is.Not.EqualTo(DoorState.Closed), "The retrieved door did not react to the button.");

            // The toolbox must hold exactly the duck the player left in it: the prototype fill
            // (mechanical tools) must not run again on load.
            var toolStorage = entMan.GetComponent<StorageComponent>(foundToolbox!.Value);
            var contents = toolStorage.Container.ContainedEntities.ToList();
            Assert.That(contents.Count, Is.EqualTo(1),
                $"The toolbox came back with {contents.Count} items instead of the single duck.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(contents[0]).EntityPrototype?.ID, Is.EqualTo("ToyRubberDuck"),
                "The toolbox lost the item it was stored with.");
        });

        await pair.CleanReturnAsync();
    }
}
