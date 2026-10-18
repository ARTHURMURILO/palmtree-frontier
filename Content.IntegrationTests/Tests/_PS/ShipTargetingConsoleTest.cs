using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server._PS.ShipCombat;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the tactical console device - the ship combat state it ensures on its grid, its BUI
/// messages, the radar range and the contact tracking.
/// </summary>
[TestFixture]
public sealed class ShipTargetingConsoleTest
{
    [Test]
    public async Task ConsoleEnsuresShipCombatOnItsGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = testMap.Grid.Owner;

        EntityUid console = default;
        await server.WaitPost(() =>
        {
            console = entMan.SpawnEntity("ComputerTacticalConsole", testMap.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ShipCombatComponent>(grid), Is.True,
                "Placing a tactical console should give the grid ship combat state.");

            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(console, out var state), Is.True);
            Assert.That(state.Owner, Is.EqualTo(grid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleMessagesUpdateShipState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = testMap.Grid.Owner;
        var bus = server.EntMan.EventBus;

        EntityUid console = default;
        await server.WaitPost(() =>
        {
            console = entMan.SpawnEntity("ComputerTacticalConsole", testMap.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);

            // The handlers are the same ones the BUI raises; the UI does not have to be open for
            // the server-side logic to be exercised.
            bus.RaiseLocalEvent(console, new ShipConsoleSetConditionMessage(ShipCondition.Yellow));
            Assert.That(state.Comp.Condition, Is.EqualTo(ShipCondition.Yellow));

            bus.RaiseLocalEvent(console, new TacticalConsoleSetAutomaticConditionMessage(false));
            Assert.That(state.Comp.AutomaticCondition, Is.False);

            bus.RaiseLocalEvent(console, new TacticalConsoleSetAutomaticFireMessage(true));
            Assert.That(state.Comp.AutomaticFire, Is.True);

            bus.RaiseLocalEvent(console, new TacticalConsoleSetAutomaticRetaliationMessage(true));
            Assert.That(state.Comp.AutomaticRetaliation, Is.True);

            bus.RaiseLocalEvent(console, new TacticalConsoleSetWeaponModeMessage(ShipWeaponMode.Primary));
            Assert.That(state.Comp.WeaponMode, Is.EqualTo(ShipWeaponMode.Primary));

            // An undefined mode is ignored.
            bus.RaiseLocalEvent(console, new TacticalConsoleSetWeaponModeMessage((ShipWeaponMode) 200));
            Assert.That(state.Comp.WeaponMode, Is.EqualTo(ShipWeaponMode.Primary));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleBuildsStateWithTacticalRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = testMap.Grid.Owner;

        EntityUid console = default;
        await server.WaitPost(() =>
        {
            console = entMan.SpawnEntity("ComputerTacticalConsole", testMap.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            var system = server.System<ShipTargetingConsoleSystem>();
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);

            var uiState = system.BuildState(console, state.Comp);
            Assert.That(uiState.Condition, Is.EqualTo(ShipCondition.Green));
            Assert.That(uiState.WeaponMode, Is.EqualTo(ShipWeaponMode.MasterSafe));
            Assert.That(uiState.AutomaticCondition, Is.True);
            Assert.That(uiState.NavState, Is.Not.Null);
            Assert.That(uiState.NavState.MaxRange, Is.EqualTo(512f),
                "The tactical console should have double the shuttle console's radar range.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleTracksContactsInRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xformSystem = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid consoleGrid = default;
        EntityUid nearGrid = default;
        EntityUid plainGrid = default;

        await server.WaitPost(() =>
        {
            mapSystem.CreateMap(out var mapId);

            var grid = mapMan.CreateGridEntity(mapId);
            consoleGrid = grid.Owner;
            mapSystem.SetTile(consoleGrid, grid.Comp, new Vector2i(0, 0), new Tile(1));
            console = entMan.SpawnEntity("ComputerTacticalConsole",
                new EntityCoordinates(consoleGrid, new Vector2(0.5f, 0.5f)));

            // A second grid 100 tiles away with a shuttle console: inside the 512-tile tactical
            // range and a ship, so it counts as a contact.
            var near = mapMan.CreateGridEntity(mapId);
            nearGrid = near.Owner;
            mapSystem.SetTile(nearGrid, near.Comp, new Vector2i(0, 0), new Tile(1));
            entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(nearGrid, new Vector2(0.5f, 0.5f)));
            xformSystem.SetWorldPosition(nearGrid, new Vector2(100f, 0f));

            // A grid without a shuttle console (an asteroid/wreck): in range, but not a ship, so it
            // must not be a contact.
            var plain = mapMan.CreateGridEntity(mapId);
            plainGrid = plain.Owner;
            mapSystem.SetTile(plainGrid, plain.Comp, new Vector2i(0, 0), new Tile(1));
            xformSystem.SetWorldPosition(plainGrid, new Vector2(-100f, 0f));
        });

        // The contact scan runs once a second.
        await pair.RunTicksSync(70);

        await server.WaitAssertion(() =>
        {
            var system = server.System<ShipTargetingConsoleSystem>();
            var contacts = system.GetContacts(console);
            Assert.That(contacts, Is.Not.Null, "The console has not scanned for contacts yet.");
            Assert.That(contacts, Does.Contain(nearGrid), "A ship inside the range was not picked up.");
            Assert.That(contacts, Does.Not.Contain(consoleGrid), "The console's own grid is not a contact.");
            Assert.That(contacts, Does.Not.Contain(plainGrid),
                "A grid without a shuttle console (an asteroid/wreck) is not a ship contact.");
        });

        await server.WaitPost(() =>
        {
            xformSystem.SetWorldPosition(nearGrid, new Vector2(5000f, 0f));
        });

        await pair.RunTicksSync(70);

        await server.WaitAssertion(() =>
        {
            var system = server.System<ShipTargetingConsoleSystem>();
            var contacts = system.GetContacts(console);
            Assert.That(contacts, Is.Not.Null);
            Assert.That(contacts, Does.Not.Contain(nearGrid),
                "A grid that left the range should stop being a contact.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DockLoadRestoresShipCombatState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid shipGrid = default;
        EntityUid loadedGrid = default;

        await server.WaitPost(() =>
        {
            var grid = mapMan.CreateGridEntity(testMap.MapId);
            shipGrid = grid.Owner;
            mapSystem.SetTile(shipGrid, grid.Comp, new Vector2i(0, 0), new Tile(1));
            entMan.SpawnEntity("ComputerTacticalConsole", new EntityCoordinates(shipGrid, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var path = BluespaceDockSystem.GetShipFilePath("ship-combat-selfheal");
            Assert.That(dock.TrySaveShipFile(shipGrid, path, out var error), Is.True, $"Saving failed: {error}");
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            dock.DeleteShipFile(path);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // A file saved before the feature (or one whose grid state was stripped) must get its
            // ship combat state back on load.
            Assert.That(entMan.RemoveComponent<ShipCombatComponent>(loadedGrid), Is.True);
            server.System<BluespaceDockSystem>().RestoreMapInitRegistration(loadedGrid);
            Assert.That(entMan.HasComponent<ShipCombatComponent>(loadedGrid), Is.True,
                "The dock's registration sweep did not restore the ship combat state.");
        });

        await pair.CleanReturnAsync();
    }
}
