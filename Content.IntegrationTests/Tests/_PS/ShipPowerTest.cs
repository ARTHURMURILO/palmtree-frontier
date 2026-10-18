using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server._PS.ShipCombat;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
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
/// Palmtree: the bluespace power budget (pools, presets), remote power and the bluespace thruster.
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
            Assert.That(Total(state.Comp), Is.EqualTo(200));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(50));

            // Raising one pool takes points from the others proportionally.
            power.SetPool(state, ShipPowerPool.Weapons, 100);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(100));
            Assert.That(Total(state.Comp), Is.EqualTo(200));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.LessThan(50),
                "Raising weapons should lower the other pools.");

            // Out-of-range values are clamped.
            power.SetPool(state, ShipPowerPool.Weapons, 500);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.LessThanOrEqualTo(125));
            Assert.That(Total(state.Comp), Is.EqualTo(200));

            power.SetPool(state, ShipPowerPool.Weapons, 0);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.GreaterThanOrEqualTo(15));
            Assert.That(Total(state.Comp), Is.EqualTo(200));

            // Locked pools do not move.
            ShipPowerSystem.SetPoolLocked(state.Comp, ShipPowerPool.Auxiliary, true);
            var auxiliary = ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Auxiliary);
            power.SetPool(state, ShipPowerPool.Shields, 125);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Auxiliary), Is.EqualTo(auxiliary),
                "A locked pool must not move.");
            Assert.That(Total(state.Comp), Is.EqualTo(200));
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

            power.ApplyPreset(state, ShipPowerPreset.Attack);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(100));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(50));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Thrusters), Is.EqualTo(25));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Auxiliary), Is.EqualTo(25));

            power.ApplyPreset(state, ShipPowerPreset.Defense);
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(100));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Weapons), Is.EqualTo(50));

            power.ApplyPreset(state, ShipPowerPreset.Balanced);
            Assert.That(Total(state.Comp), Is.EqualTo(200));
            Assert.That(ShipPowerSystem.GetPool(state.Comp, ShipPowerPool.Shields), Is.EqualTo(50));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SinkPowersRemoteDevices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid sink = default;
        EntityUid device = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));
            sink = entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            AnchorIfNeeded(entMan, xform, sink);

            // A stand-in remote device: an entity with a receiver and the bluespace powered component.
            device = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var receiver = entMan.AddComponent<ApcPowerReceiverComponent>(device);
            receiver.NeedsPower = false;
            receiver.PowerDisabled = true;

            var powered = entMan.AddComponent<BluespacePoweredComponent>(device);
            powered.Pool = ShipPowerPool.Thrusters;
            powered.RatedPower = 10000f;

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.False,
                "The sink should power the remote device.");
            Assert.That(entMan.GetComponent<BluespacePoweredComponent>(device).Performance,
                Is.EqualTo(1f).Within(0.01f), "Thrusters at 50 should give 100% performance.");

            // The Auxiliary share goes back to the grid: 50/200 * 100 kW = 25 kW.
            Assert.That(entMan.GetComponent<PowerNetworkBatteryComponent>(sink).MaxSupply,
                Is.EqualTo(25000f).Within(1f), "The sink should feed its auxiliary share to the grid.");

            // Dropping the pool below the operating minimum cuts the device off.
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            server.System<ShipPowerSystem>().SetPool(state, ShipPowerPool.Thrusters, 15);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.True,
                "A pool below the operating minimum must not power its devices.");
            Assert.That(entMan.GetComponent<BluespacePoweredComponent>(device).Performance, Is.EqualTo(0f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmptyBatteryBrownsOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid sink = default;
        EntityUid device = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));
            sink = entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            AnchorIfNeeded(entMan, xform, sink);

            device = entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var receiver = entMan.AddComponent<ApcPowerReceiverComponent>(device);
            receiver.NeedsPower = false;
            receiver.PowerDisabled = true;

            var powered = entMan.AddComponent<BluespacePoweredComponent>(device);
            powered.Pool = ShipPowerPool.Thrusters;
            powered.RatedPower = 10000f;

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.False);

            // An empty sink cannot power anything.
            var battery = entMan.GetComponent<BatteryComponent>(sink);
            server.System<BatterySystem>().SetCharge(sink, 0f, battery);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(device).PowerDisabled, Is.True,
                "An empty sink should brown out its devices.");
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
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid sink = default;
        EntityUid thruster = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0), new Vector2i(1, 0));
            sink = entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            AnchorIfNeeded(entMan, xform, sink);

            thruster = entMan.SpawnEntity("BluespaceThruster", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

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
            Assert.That(thrusterComp.Thrust, Is.EqualTo(150f).Within(0.1f),
                "Thrusters at 100 should run at 150% of their parts thrust.");

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
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid loadedGrid = default;

        await server.WaitPost(() =>
        {
            grid = CreateGrid(entMan, mapSystem, mapMan, testMap.MapId, new Vector2i(0, 0));
            var sink = entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            AnchorIfNeeded(entMan, xform, sink);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryEnsureShipCombat(grid, out var state), Is.True);
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
            Assert.That(ShipPowerSystem.GetPool(combat!, ShipPowerPool.Weapons), Is.EqualTo(100),
                "The power pools should survive a dock round trip.");
            Assert.That(ShipPowerSystem.GetPool(combat!, ShipPowerPool.Auxiliary), Is.EqualTo(25));
        });

        await pair.CleanReturnAsync();
    }

    private static void AnchorIfNeeded(IEntityManager entMan, SharedTransformSystem xform, EntityUid uid)
    {
        if (!entMan.GetComponent<TransformComponent>(uid).Anchored)
            xform.AnchorEntity(uid);
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
                     ShipPowerPool.Auxiliary,
                 })
        {
            sum += ShipPowerSystem.GetPool(combat, pool);
        }

        return sum;
    }
}
