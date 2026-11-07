using System.Numerics;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._PS.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS.Atmos;

/// <summary>
/// Palmtree: behaviour tests for the heat pump, the two-port thermomachine ported from /tg/station.
/// </summary>
/// <remarks>
/// The heat pump's gas port faces south and its waste port north, so the tests spawn one pipe on each
/// side, seed both mixtures by hand and raise <see cref="AtmosDeviceUpdateEvent"/> directly.
/// </remarks>
[TestFixture]
public sealed class HeatPumpTest
{
    private static readonly EntProtoId PipeProto = "GasPipeStraight";

    [Test]
    public async Task HeatPumpCoolsTheGasIntoTheWasteLoop()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture gas = default!;
            GasMixture waste = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 300f;
                pump.MotorHeat = 0f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                (gas, waste) = GetAir(entMan, device);

                // Same gas and same amount on both sides, so the heat capacities cancel out.
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 500f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = 300f;
            });

            await server.WaitAssertion(() => Raise(entMan, device));

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);

                Assert.That(pump.Cooling, Is.True, "The pump should be cooling towards a colder setpoint.");
                Assert.That(gas.Temperature, Is.LessThan(500f), "The gas should be cooled.");
                Assert.That(waste.Temperature, Is.GreaterThan(300f), "The waste loop should be heated.");

                // Nothing is created or destroyed: what the gas lost, the waste loop gained.
                Assert.That(500f - gas.Temperature, Is.EqualTo(waste.Temperature - 300f).Within(0.5f),
                    "Heat must be conserved between the two ports.");
                Assert.That(pump.Efficiency, Is.GreaterThan(0.5f));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task HeatPumpHeatsTheGasFromTheWasteLoop()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture gas = default!;
            GasMixture waste = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 500f;
                pump.MotorHeat = 0f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                (gas, waste) = GetAir(entMan, device);

                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 300f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = 500f;
            });

            await server.WaitAssertion(() => Raise(entMan, device));

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);

                Assert.That(pump.Cooling, Is.False, "The pump should be heating towards a hotter setpoint.");
                Assert.That(gas.Temperature, Is.GreaterThan(300f), "The gas should be heated.");
                Assert.That(waste.Temperature, Is.LessThan(500f), "The waste loop should be cooled.");

                Assert.That(gas.Temperature - 300f, Is.EqualTo(500f - waste.Temperature).Within(0.5f),
                    "Heat must be conserved between the two ports.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// The whole point of the machine: cooling into a waste loop that is already hot is worse than
    /// cooling into a cold one.
    /// </summary>
    [Test]
    public async Task HeatPumpIsLessEfficientAgainstAHotWasteLoop()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture gas = default!;
            GasMixture waste = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            var coldEfficiency = 0f;
            var hotEfficiency = 0f;

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 250f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                (gas, waste) = GetAir(entMan, device);
            });

            // Cold waste loop.
            await server.WaitAssertion(() =>
            {
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 400f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = 300f;

                Raise(entMan, device);
                coldEfficiency = entMan.GetComponent<GasHeatPumpComponent>(device).Efficiency;
            });

            // Same machine, same gas, but the waste loop is now hot.
            await server.WaitAssertion(() =>
            {
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 400f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = 900f;

                Raise(entMan, device);
                hotEfficiency = entMan.GetComponent<GasHeatPumpComponent>(device).Efficiency;
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(coldEfficiency, Is.GreaterThan(0.5f));
                Assert.That(hotEfficiency, Is.LessThan(coldEfficiency),
                    "A hot waste loop must be worse to cool into than a cold one.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// A side with almost no gas in it cannot carry heat, so the machine throttles itself.
    /// </summary>
    [Test]
    public async Task HeatPumpThrottlesOnAnEmptyWasteLoop()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture gas = default!;
            GasMixture waste = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            var fullEfficiency = 0f;
            var emptyEfficiency = 0f;

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 250f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                (gas, waste) = GetAir(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 400f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = 300f;

                Raise(entMan, device);
                fullEfficiency = entMan.GetComponent<GasHeatPumpComponent>(device).Efficiency;
            });

            await server.WaitAssertion(() =>
            {
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 400f;
                waste.Clear();
                waste.Temperature = 300f;

                Raise(entMan, device);
                emptyEfficiency = entMan.GetComponent<GasHeatPumpComponent>(device).Efficiency;
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(fullEfficiency, Is.GreaterThan(0.5f));
                Assert.That(emptyEfficiency, Is.LessThan(fullEfficiency),
                    "An empty waste loop must slow the machine down.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task HeatPumpDoesNothingWithoutPower()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture gas = default!;
            GasMixture waste = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 250f;

                (gas, waste) = GetAir(entMan, device);

                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 400f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = 300f;
            });

            await server.WaitAssertion(() => Raise(entMan, device));

            await server.WaitAssertion(() =>
            {
                Assert.That(gas.Temperature, Is.EqualTo(400f).Within(0.01f),
                    "An unpowered heat pump must not move any heat.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// Cooling with no waste pipe at all has nowhere to put the heat, so the gas must not change.
    /// </summary>
    [Test]
    public async Task HeatPumpDoesNothingWithoutAWasteLoop()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture gas = default!;

            await server.WaitPost(() =>
            {
                // Gas port piped in, waste port left hanging.
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasHeatPump", coords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 250f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                gas = GetAir(entMan, device).gas;
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 400f;
            });

            await server.WaitAssertion(() => Raise(entMan, device));

            await server.WaitAssertion(() =>
            {
                Assert.That(gas.Temperature, Is.EqualTo(400f).Within(0.01f),
                    "Without a waste loop there is nowhere for the heat to go.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static (GasMixture gas, GasMixture waste) GetAir(IEntityManager entMan, EntityUid device)
    {
        var container = entMan.GetComponent<NodeContainerComponent>(device);
        var gas = (PipeNode) container.Nodes["gas"];
        var waste = (PipeNode) container.Nodes["waste"];

        return (gas.Air, waste.Air);
    }

    private static void Raise(IEntityManager entMan, EntityUid device)
    {
        var ev = new AtmosDeviceUpdateEvent(1f, null, null);
        entMan.EventBus.RaiseLocalEvent(device, ref ev);
    }
}
