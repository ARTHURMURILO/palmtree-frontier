using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server._PS.ShipRegistry;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._PS.ShipRegistry;
using Content.Shared.Shuttles.Components;
using Content.Server._NF.SectorServices;
using Content.Shared.Station;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the Ship Registrar turns a docked, unregistered grid into a civilian vessel (station,
/// deed, record and marker) that the Bluespace Dock accepts.
/// </summary>
[TestFixture]
public sealed class ShipRegistrarTest
{
    [Test]
    public async Task RegisteringAGridMakesItACivilianVessel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            var stationSystem = server.System<StationSystem>();
            var dockingSystem = server.System<DockingSystem>();
            var registrar = server.System<ShipRegistrarSystem>();
            var dock = server.System<BluespaceDockSystem>();

            var stationGrid = testMap.Grid.Owner;
            var mapSystem = server.System<SharedMapSystem>();
            var xformSystem = server.System<SharedTransformSystem>();

            // A station with the registrar console on it.
            stationSystem.InitializeNewStation(
                new StationConfig { StationPrototype = "StandardFrontierVessel" },
                new List<EntityUid> { stationGrid },
                "Test Station");
            var console = entMan.SpawnEntity("ComputerShipRegistrar", new EntityCoordinates(stationGrid, new Vector2(0.5f, 0.5f)));

            // A hand-built ship grid next to the station, with a dock of its own. Tiles are required:
            // an airlock spawned on a tile-less cell gets reparented onto whatever grid has a tile
            // there (or the map).
            var shipGridEnt = mapMan.CreateGridEntity(testMap.MapId);
            var shipGrid = shipGridEnt.Owner;
            xformSystem.SetLocalPosition(shipGrid, new Vector2(2f, 0f));
            mapSystem.SetTiles(shipGrid, shipGridEnt.Comp, new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)) });

            var stationDock = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(stationGrid, new Vector2(0.5f, 0.5f)));
            var shipDock = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(shipGrid, new Vector2(0.5f, 0.5f)));
            dockingSystem.Dock(
                (stationDock, entMan.GetComponent<DockingComponent>(stationDock)),
                (shipDock, entMan.GetComponent<DockingComponent>(shipDock)));

            var targetId = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(stationGrid, Vector2.Zero));

            // The shuttle records store lives on the sector service entity, which a station map
            // creates through its sector service host component.
            var serviceHost = entMan.SpawnEntity(null, new EntityCoordinates(stationGrid, Vector2.Zero));
            entMan.AddComponent<StationSectorServiceHostComponent>(serviceHost);

            // Unregistered, docked grids are offered by the registrar.
            var offered = registrar.GetDockableGrids(console);
            Assert.That(offered.Any(g => g.Grid == entMan.GetNetEntity(shipGrid)), Is.True,
                "An unregistered grid docked to the station should be listed.");

            // Register it.
            Assert.That(
                registrar.TryRegisterVessel(console, stationGrid, targetId, shipGrid, "Test Ship", "TS-001", out var error),
                Is.True,
                $"Registration failed: {error}");

            // Deed on the ship and on the owner's ID.
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(shipGrid), Is.True, "The ship should get a deed.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(targetId), Is.True, "The owner's ID should get a deed.");

            var deed = entMan.GetComponent<ShuttleDeedComponent>(targetId);
            Assert.That(deed.ShuttleUid, Is.EqualTo(shipGrid));
            Assert.That(deed.ShuttleName, Is.EqualTo("Test Ship"));
            Assert.That(deed.ShuttleNameSuffix, Is.EqualTo("TS-001"));

            // Civilian marker and its own station.
            var registered = entMan.GetComponent<RegisteredVesselComponent>(shipGrid);
            Assert.That(registered.Model, Is.EqualTo("Custom"));
            Assert.That(registered.Group.ToString(), Is.EqualTo("Shipyard"));
            Assert.That(stationSystem.GetOwningStation(shipGrid), Is.Not.Null, "A registered ship gets its own station.");

            // Player-shuttle IFF: this is what gives the ship a label on the radar/mass scanner.
            Assert.That(entMan.TryGetComponent<IFFComponent>(shipGrid, out var iff), Is.True,
                "A registered ship must carry IFF or the radar cannot label it.");
            Assert.That((iff!.Flags & IFFFlags.IsPlayerShuttle) != 0, Is.True,
                "Registered ships are player shuttles.");

            // A warp point, like shipyard ships carry in their files, so `warp` and the ghost warp
            // menu can reach it.
            var warp = dock.FindShipWarpPoint(shipGrid);
            Assert.That(warp, Is.Not.Null, "A registered ship must have a warp point.");
            Assert.That(warp!.Value.Comp.Location, Is.EqualTo("Test Ship TS-001"));
            Assert.That(warp.Value.Comp.UseStationName, Is.True, "The warp name must follow the ship's name.");

            // Renaming the ship's station (which is what a rename does) updates the warp point.
            var shipStation = stationSystem.GetOwningStation(shipGrid);
            Assert.That(shipStation, Is.Not.Null, "A registered ship gets its own station.");
            stationSystem.RenameStation(shipStation!.Value, "Renamed Ship", loud: false);
            Assert.That(dock.FindShipWarpPoint(shipGrid)!.Value.Comp.Location, Is.EqualTo("Renamed Ship"),
                "The warp point must follow the ship's name.");

            // The Bluespace Dock now sees a storable civilian vessel.
            Assert.That(dock.TryGetVesselInfo(shipGrid, out var info), Is.True, "The registered ship should have a vessel identity.");
            Assert.That(BluespaceDockSystem.IsAllowedGroup(info.Group), Is.True, "Registered ships must count as civilian.");

            // And it is not offered for registration again.
            Assert.That(registrar.GetDockableGrids(console).Any(g => g.Grid == entMan.GetNetEntity(shipGrid)), Is.False,
                "A registered ship should not be offered again.");
        });

        await pair.CleanReturnAsync();
    }
}
