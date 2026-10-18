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

            bus.RaiseLocalEvent(console, new ShieldingConsoleSetPoolMessage(ShipPowerPool.Weapons, 100));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(100));
            Assert.That(Total(state.Comp), Is.EqualTo(200));

            bus.RaiseLocalEvent(console, new ShieldingConsoleSetPoolLockMessage(ShipPowerPool.Auxiliary, true));
            Assert.That(state.Comp.AuxiliaryPowerLocked, Is.True);

            bus.RaiseLocalEvent(console, new ShieldingConsoleSetPresetMessage(ShipPowerPreset.Defense));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(100));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(50));

            bus.RaiseLocalEvent(console, new ShipConsoleSetConditionMessage(ShipCondition.Yellow));
            Assert.That(state.Comp.Condition, Is.EqualTo(ShipCondition.Yellow));
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

            entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
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

            // Cut the shield generator off: the alert appears.
            var powered = entMan.GetComponent<BluespacePoweredComponent>(
                FindGenerator(entMan, grid));
            powered.Powered = false;

            uiState = consoleSystem.BuildState(console, state.Comp);
            Assert.That(uiState.ShieldsAlert, Is.True, "An unpowered generator should raise the shields alert.");
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
                     ShipPowerPool.Auxiliary,
                 })
        {
            sum += ShipPowerSystem.GetPool(combat, pool);
        }

        return sum;
    }
}
