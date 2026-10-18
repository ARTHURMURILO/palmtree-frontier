using System.Collections.Generic;
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._PS.BluespaceDock;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: a stored ship must come back with its power working. Machines draw power through the
/// extension-cable field of the nearest APC (there is no cabling on most ships), so a retrieved ship
/// whose machines all report "un-powered" until they are deconstructed and rebuilt means the APC
/// link did not come back with the grid.
/// </summary>
[TestFixture]
public sealed class BluespaceDockPowerTest
{
    [Test]
    public async Task SavedShipMachinesStayPowered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid shipGrid = default;
        EntityUid loadedGrid = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            var xformSystem = server.System<SharedTransformSystem>();

            shipGrid = mapMan.CreateGridEntity(testMap.MapId).Owner;
            mapSystem.SetTiles(shipGrid, entMan.GetComponent<MapGridComponent>(shipGrid),
                new List<(Vector2i, Tile)>
                {
                    (new Vector2i(0, 0), new Tile(1)),
                    (new Vector2i(1, 0), new Tile(1)),
                    (new Vector2i(2, 0), new Tile(1)),
                });

            // The ship's APC (a charged battery: APCBasic starts at 50kJ) and a console within its
            // extension-cable range, both anchored like a real machine room.
            var apc = entMan.SpawnEntity("APCBasic", new EntityCoordinates(shipGrid, new Vector2(1.5f, 0.5f)));
            Anchor(xformSystem, entMan, apc);

            var computer = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(shipGrid, new Vector2(0.5f, 0.5f)));
            Anchor(xformSystem, entMan, computer);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var computer = FindComputer(entMan, server.System<BluespaceDockSystem>(), shipGrid);
            Assert.That(computer, Is.Not.Null, "The ship has no console.");
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(computer!.Value).Powered, Is.True,
                "Sanity check failed: the console is not powered before the ship is stored.");

            var dock = server.System<BluespaceDockSystem>();
            var path = BluespaceDockSystem.GetShipFilePath("power-roundtrip");
            Assert.That(dock.TrySaveShipFile(shipGrid, path, out var error), Is.True, $"Saving failed: {error}");
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            dock.DeleteShipFile(path);
        });

        // Give the loaded grid a chance to rebuild its power and node networks on its own.
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var computer = FindComputer(entMan, server.System<BluespaceDockSystem>(), loadedGrid);
            Assert.That(computer, Is.Not.Null, "The retrieved ship has no console.");

            var receiver = entMan.GetComponent<ApcPowerReceiverComponent>(computer!.Value);
            Assert.That(receiver.Powered, Is.True,
                "The retrieved ship's console reports un-powered: the APC's power did not come back " +
                "with the ship (a player would have to deconstruct and rebuild the machine).");

            // Machines that lose their link (a load that did not reconnect them, a machine pulled off
            // the APC by an update) are picked up by the load-time registration sweep again instead
            // of staying "un-powered" until they are rebuilt.
            var cableReceiver = entMan.GetComponent<ExtensionCableReceiverComponent>(computer.Value);
            server.System<ExtensionCableSystem>().Disconnect((computer.Value, cableReceiver));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var computer = FindComputer(entMan, server.System<BluespaceDockSystem>(), loadedGrid);
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(computer!.Value).Powered, Is.False,
                "The console should be un-powered after losing its link.");

            server.System<BluespaceDockSystem>().RestoreMapInitRegistration(loadedGrid);
            Assert.That(entMan.GetComponent<ExtensionCableReceiverComponent>(computer.Value).Provider, Is.Not.Null,
                "The registration sweep did not re-link the console to the ship's APC.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var computer = FindComputer(entMan, server.System<BluespaceDockSystem>(), loadedGrid);
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(computer!.Value).Powered, Is.True,
                "The console is still un-powered after the registration sweep re-linked it.");
        });

        await pair.CleanReturnAsync();
    }

    private static void Anchor(SharedTransformSystem xformSystem, IEntityManager entMan, EntityUid uid)
    {
        if (!entMan.GetComponent<TransformComponent>(uid).Anchored)
            xformSystem.AnchorEntity(uid);
    }

    private static EntityUid? FindComputer(IEntityManager entMan, BluespaceDockSystem dock, EntityUid grid)    {
        var entities = new HashSet<EntityUid>();
        dock.GetEntitiesOnGrid(grid, entities);

        foreach (var uid in entities)
        {
            if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ComputerShuttle")
                return uid;
        }

        return null;
    }
}
