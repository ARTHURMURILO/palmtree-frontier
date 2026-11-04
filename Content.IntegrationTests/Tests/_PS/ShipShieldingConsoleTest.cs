using System.Numerics;
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
/// Palmtree: the engineering shielding console (power pools, presets, locks, condition and the
/// shield state it renders).
/// </summary>
[TestFixture]
public sealed class ShipShieldingConsoleTest
{
    [Test]
    public async Task ConsoleEditsPoolsPresetsAndCondition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var bus = server.EntMan.EventBus;

        EntityUid console = default;
        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(0, 0), new Tile(1));
            console = entMan.SpawnEntity("ComputerShieldingConsole",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True,
                "Placing a shielding console should give the grid ship combat state.");

            MarkPoolsPresent(state.Comp);

            bus.RaiseLocalEvent(console, new ShieldingConsoleSetPoolMessage(ShipPowerPool.Weapons, 100));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(100));
            Assert.That(Total(state.Comp), Is.EqualTo(150));

            bus.RaiseLocalEvent(console, new ShieldingConsoleSetPoolLockMessage(ShipPowerPool.Thrusters, true));
            Assert.That(state.Comp.ThrusterPowerLocked, Is.True);

            MarkPoolsPresent(state.Comp);
            bus.RaiseLocalEvent(console, new ShieldingConsoleSetPresetMessage(ShipPowerPreset.Defense));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(120));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(15));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(15));

            bus.RaiseLocalEvent(console, new ShipConsoleSetConditionMessage(ShipCondition.Yellow));
            Assert.That(state.Comp.Condition, Is.EqualTo(ShipCondition.Yellow));

            // The Silent checkbox is per console and travels into the window state.
            Assert.That(server.System<ShipShieldingConsoleSystem>().BuildState(console, state.Comp).Silent, Is.False,
                "Consoles are audible by default.");
            bus.RaiseLocalEvent(console, new ShieldingConsoleSetSilentMessage(true));
            Assert.That(entMan.GetComponent<ShipShieldingConsoleComponent>(console).Silent, Is.True);
            Assert.That(server.System<ShipShieldingConsoleSystem>().BuildState(console, state.Comp).Silent, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleStateShowsShieldsAndAlerts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid console = default;
        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(0, 0), new Tile(1));
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(1, 0), new Tile(1));

            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            entMan.SpawnEntity("ShieldGenerator", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            console = entMan.SpawnEntity("ComputerShieldingConsole",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var consoleSystem = server.System<ShipShieldingConsoleSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);

            var uiState = consoleSystem.BuildState(console, state.Comp);
            Assert.That(uiState.MaxPerFacing, Is.EqualTo(300f).Within(1f));
            Assert.That(uiState.Facings[(int) ShieldFacing.Fore], Is.EqualTo(300f).Within(1f),
                "The window should show full facings.");
            Assert.That(uiState.ShieldsAlert, Is.False, "A powered generator should not raise the shields alert.");
            Assert.That(uiState.ThrustersAlert, Is.True, "A ship without bluespace thrusters should alert.");

            Assert.That(uiState.ActivePools, Has.Length.EqualTo(3));
            Assert.That(uiState.ActivePools[(int) ShipPowerPool.Shields], Is.True);
            Assert.That(uiState.ActivePools[(int) ShipPowerPool.Weapons], Is.False,
                "A ship without weapons has no weapons pool.");
            Assert.That(uiState.ActivePools[(int) ShipPowerPool.Thrusters], Is.False,
                "A ship without bluespace thrusters has no thrusters pool.");

            // Cut the shield generator off: the alert appears.
            var powered = entMan.GetComponent<BluespacePoweredComponent>(
                FindGenerator(entMan, grid));
            powered.Powered = false;

            uiState = consoleSystem.BuildState(console, state.Comp);
            Assert.That(uiState.ShieldsAlert, Is.True, "An unpowered generator should raise the shields alert.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TabletopConsolesKeepTheirConsoleSystems()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(0, 0), new Tile(1));
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(1, 0), new Tile(1));
            entMan.SpawnEntity("ComputerTabletopShieldingConsole",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entMan.SpawnEntity("ComputerTabletopTacticalConsole",
                new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var shielding = entMan.EntityQueryEnumerator<ShipShieldingConsoleComponent>();
            Assert.That(shielding.MoveNext(out _, out _), Is.True,
                "The tabletop engineering console should keep the shielding console system.");

            var targeting = entMan.EntityQueryEnumerator<ShipTargetingConsoleComponent>();
            Assert.That(targeting.MoveNext(out _, out _), Is.True,
                "The tabletop tactical console should keep the targeting console system.");

            Assert.That(server.System<ShipCombatSystem>().TryGetShipCombat(grid, out _), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid FindGenerator(IEntityManager entMan, EntityUid grid)
    {
        var query = entMan.EntityQueryEnumerator<ShieldGeneratorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return uid;
        }

        Assert.Fail("No shield generator on the grid.");
        return default;
    }

    private static int Total(ShipCombatComponent combat)
    {
        var sum = 0;
        foreach (var pool in new[]
                 {
                     ShipPowerPool.Shields,
                     ShipPowerPool.Weapons,
                     ShipPowerPool.Thrusters,
                 })
        {
            sum += ShipPowerSystem.GetPool(combat, pool);
        }

        return sum;
    }

    /// <summary>Test grids have no devices; presets skip absent systems, so mark them present.</summary>
    private static void MarkPoolsPresent(Content.Shared._PS.ShipCombat.Components.ShipCombatComponent combat)
    {
        combat.ShieldPoolActive = true;
        combat.WeaponPoolActive = true;
        combat.ThrusterPoolActive = true;
    }
}
