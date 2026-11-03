using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server._PS.ShipCombat;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
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
/// Palmtree: the bluespace power budget (pools, presets), the transmitter's grid draw, remote power
/// and the bluespace thruster.
/// </summary>
[TestFixture]
public sealed class ShipPowerTest
{
    [Test]
    public async Task PoolsStayWithinBudget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var power = server.System<ShipPowerSystem>();
            Assert.That(combat.TryEnsureShipCombat(testMap.Grid.Owner, out var state), Is.True);

            // Defaults are balanced.
            Assert.That(Total(state.Comp), Is.EqualTo(150));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(50));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(50));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(50));

            // Raising one pool takes points from the others proportionally.
            power.SetPool(state, ShipPowerPool.Weapons, 100);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(100));
            Assert.That(Total(state.Comp), Is.EqualTo(150));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.LessThan(50),
                "Raising weapons should lower the other pools.");

            // Out-of-range values are clamped.
            power.SetPool(state, ShipPowerPool.Weapons, 500);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.LessThanOrEqualTo(125));
            Assert.That(Total(state.Comp), Is.EqualTo(150));

            power.SetPool(state, ShipPowerPool.Weapons, 0);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.GreaterThanOrEqualTo(15));
            Assert.That(Total(state.Comp), Is.EqualTo(150));

            // Locked pools do not move.
            ShipPowerSystem.SetPoolLocked(state.Comp, ShipPowerPool.Thrusters, true);
            var thrusters = ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters);
            power.SetPool(state, ShipPowerPool.Shields, 125);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(thrusters),
                "A locked pool must not move.");
            Assert.That(Total(state.Comp), Is.EqualTo(150));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PresetsApply()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var power = server.System<ShipPowerSystem>();
            Assert.That(combat.TryEnsureShipCombat(testMap.Grid.Owner, out var state), Is.True);

            MarkPoolsPresent(state.Comp);
            power.ApplyPreset(state, ShipPowerPreset.Attack);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(120));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(15));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(15));
            Assert.That(Total(state.Comp), Is.EqualTo(150));

            MarkPoolsPresent(state.Comp);
            power.ApplyPreset(state, ShipPowerPreset.Defense);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(120));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(15));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(15));

            MarkPoolsPresent(state.Comp);
            power.ApplyPreset(state, ShipPowerPreset.Balanced);
            Assert.That(Total(state.Comp), Is.EqualTo(150));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(50));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(50));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(50));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PresetsSkipUnavailablePools()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var power = server.System<ShipPowerSystem>();
            Assert.That(combat.TryEnsureShipCombat(testMap.Grid.Owner, out var state), Is.True);

            // Only weapons are fitted; shields and thrusters are absent.
            state.Comp.ShieldPoolActive = false;
            state.Comp.WeaponPoolActive = true;
            state.Comp.ThrusterPoolActive = false;
            state.Comp.ShieldPower = 90;
            state.Comp.ThrusterPower = 80;

            power.ApplyPreset(state, ShipPowerPreset.Attack);

            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(120));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(90),
                "An unavailable pool must keep its value.");
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(80),
                "An unavailable pool must keep its value.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransmitterPowersRemoteDevices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid sink = default;
        EntityUid device = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));
            sink = ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));

            // A stand-in remote device: an entity with a receiver and the bluespace powered component.
            device = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var receiver = entMan.AddComponent<ApcPowerReceiverComponent>(device);
            receiver.NeedsPower = false;
            receiver.PowerDisabled = true;

            var powered = entMan.AddComponent<BluespacePoweredComponent>(device);
            powered.Pool = ShipPowerPool.Thrusters;
            powered.RatedPower = 15000f;

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.False,
                "The transmitter should power the remote device.");
            Assert.That(entMan.GetComponent<BluespacePoweredComponent>(device).Performance,
                Is.EqualTo(50f / 150f).Within(0.01f), "Thrusters at 50 run at one third performance.");

            // The transmitter is a pure consumer: it draws the devices' demand and supplies nothing.
            Assert.That(entMan.TryGetComponent<PowerSupplierComponent>(sink, out _), Is.False,
                "The transmitter must not supply the ship's grid.");
            var consumer = entMan.GetComponent<PowerConsumerComponent>(sink);
            Assert.That(consumer.DrawRate, Is.EqualTo(5000f).Within(50f),
                "15000 W at one third performance is a 5 kW draw.");

            // Dropping the pool below the operating minimum cuts the device off. The fake device has
            // no bluespace thruster component, so mark its pool present for the public SetPool API.
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            state.Comp.ThrusterPoolActive = true;
            server.System<ShipPowerSystem>().SetPool(state, ShipPowerPool.Thrusters, 15);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.True,
                "A pool below the operating minimum must not power its devices.");
            Assert.That(entMan.GetComponent<BluespacePoweredComponent>(device).Performance, Is.EqualTo(0f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransmitterWithoutGridPowerLeavesDevicesOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid device = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));

            // A transmitter with no cable terminal or generator attached.
            entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            device = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var receiver = entMan.AddComponent<ApcPowerReceiverComponent>(device);
            receiver.NeedsPower = false;
            receiver.PowerDisabled = true;

            var powered = entMan.AddComponent<BluespacePoweredComponent>(device);
            powered.Pool = ShipPowerPool.Thrusters;
            powered.RatedPower = 15000f;

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.True,
                "A transmitter with no grid power must not power its devices.");
            Assert.That(entMan.GetComponent<BluespacePoweredComponent>(device).Performance, Is.EqualTo(0f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BluespaceThrusterScalesWithPower()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid thruster = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));
            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));

            thruster = entMan.SpawnEntity("BluespaceThruster", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            server.System<ShipPowerSystem>().SetPool(state, ShipPowerPool.Thrusters, 100);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var thrusterComp = entMan.GetComponent<ThrusterComponent>(thruster);
            Assert.That(thrusterComp.IsOn, Is.True, "A powered bluespace thruster should run.");
            Assert.That(thrusterComp.Thrust, Is.EqualTo(100f * 100f / 150f).Within(0.1f),
                "Thrusters at 100 should run at two thirds of their parts thrust.");

            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            server.System<ShipPowerSystem>().SetPool(state, ShipPowerPool.Thrusters, 15);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var thrusterComp = entMan.GetComponent<ThrusterComponent>(thruster);
            Assert.That(thrusterComp.IsOn, Is.False, "A thruster without power must shut down.");
            Assert.That(thrusterComp.Thrust, Is.EqualTo(0f).Within(0.1f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PoolsSurviveDockRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid loadedGrid = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0));
            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryEnsureShipCombat(grid, out var state), Is.True);
            MarkPoolsPresent(state.Comp);
            server.System<ShipPowerSystem>().ApplyPreset(state, ShipPowerPreset.Attack);

            var dock = server.System<BluespaceDockSystem>();
            var path = BluespaceDockSystem.GetShipFilePath("ship-power-roundtrip");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, $"Saving failed: {error}");
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            dock.DeleteShipFile(path);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<ShipCombatComponent>(loadedGrid, out var combat), Is.True,
                "The loaded ship should keep its ship combat state.");
            Assert.That(ShipPowerSystem.GetPool(combat!, ShipPowerPool.Weapons), Is.EqualTo(120),
                "The power pools should survive a dock round trip.");
            Assert.That(ShipPowerSystem.GetPool(combat!, ShipPowerPool.Shields), Is.EqualTo(15));
            Assert.That(ShipPowerSystem.GetPool(combat!, ShipPowerPool.Thrusters), Is.EqualTo(15));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConnectedTransmitterIsActiveWithoutGeneration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid sink = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));

            // HV cable under the transmitter, but no generator, so this only proves the transmitter
            // recognises a connected power net.
            sink = entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entMan.SpawnEntity("CableHV", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var consumer = entMan.GetComponent<PowerConsumerComponent>(sink);
            Assert.That(consumer.Net, Is.Not.Null, "The transmitter should be attached to a power net.");
            Assert.That(consumer.Net!.IsConnectedNetwork, Is.True, "The power net should be connected.");
            Assert.That(entMan.GetComponent<BluespacePowerSinkComponent>(sink).Active, Is.True,
                "A connected transmitter should be active even before the grid supplies power.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CableUnderTransmitterConnectsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid sink = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));

            // The standard machine wiring: a cable underneath the transmitter.
            sink = entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entMan.SpawnEntity("CableHV", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entMan.SpawnEntity("CableHV", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var consumer = entMan.GetComponent<PowerConsumerComponent>(sink);
            Assert.That(consumer.Net, Is.Not.Null, "A cable under the transmitter should attach it to a net.");
            Assert.That(consumer.Net!.IsConnectedNetwork, Is.True, "The cable net should be connected.");
            Assert.That(entMan.GetComponent<BluespacePowerSinkComponent>(sink).Active, Is.True,
                "A cable-wired transmitter should be active.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransmitterDrawsFromTheGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid sink = default;
        EntityUid device = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));
            sink = ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));

            device = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var receiver = entMan.AddComponent<ApcPowerReceiverComponent>(device);
            receiver.NeedsPower = false;
            receiver.PowerDisabled = true;

            var powered = entMan.AddComponent<BluespacePoweredComponent>(device);
            powered.Pool = ShipPowerPool.Shields;
            powered.RatedPower = 9000f;

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var consumer = entMan.GetComponent<PowerConsumerComponent>(sink);
            Assert.That(consumer.DrawRate, Is.EqualTo(3000f).Within(50f),
                "The transmitter should draw 9000 W at one third performance.");
            Assert.That(consumer.ReceivedPower, Is.GreaterThan(0f),
                "The generator should supply the transmitter's draw.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShieldGeneratorDrawsItsPassiveMaintenanceCost()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid sink = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));
            sink = ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            entMan.SpawnEntity("ShieldGenerator", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var consumer = entMan.GetComponent<PowerConsumerComponent>(sink);
            Assert.That(consumer.DrawRate, Is.EqualTo(10000f).Within(50f),
                "A shield generator should draw a fixed 10 kW while maintaining the shields.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExtraTransmittersAreInactiveBackups()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid grid = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid device = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId,
                new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0));
            first = ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            second = ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(2.5f, 0.5f));

            device = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var receiver = entMan.AddComponent<ApcPowerReceiverComponent>(device);
            receiver.NeedsPower = false;
            receiver.PowerDisabled = true;

            var powered = entMan.AddComponent<BluespacePoweredComponent>(device);
            powered.Pool = ShipPowerPool.Thrusters;
            powered.RatedPower = 15000f;

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var firstComp = entMan.GetComponent<BluespacePowerSinkComponent>(first);
            var secondComp = entMan.GetComponent<BluespacePowerSinkComponent>(second);
            var firstConsumer = entMan.GetComponent<PowerConsumerComponent>(first);
            var secondConsumer = entMan.GetComponent<PowerConsumerComponent>(second);

            Assert.That(firstComp.Active ^ secondComp.Active, Is.True,
                "Exactly one transmitter should be active.");
            Assert.That(firstConsumer.DrawRate > 0f ^ secondConsumer.DrawRate > 0f, Is.True,
                "Only the active transmitter should draw.");
        });

        // Removing the active transmitter hands over to the backup.
        await server.WaitPost(() =>
        {
            var active = entMan.GetComponent<BluespacePowerSinkComponent>(first).Active ? first : second;
            entMan.QueueDeleteEntity(active);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BluespacePowerSinkComponent>(second).Active, Is.True,
                "The backup should take over when the active transmitter is removed.");
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.False,
                "The backup should keep the remote devices powered.");
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid CreateGrid(
        IEntityManager entMan,
        SharedMapSystem mapSystem,
        IMapManager mapMan,
        MapId mapId,
        params Vector2i[] tiles)
    {
        var grid = mapMan.CreateGridEntity(mapId);
        foreach (var tile in tiles)
            mapSystem.SetTile(grid.Owner, grid.Comp, tile, new Tile(1));

        return grid.Owner;
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
