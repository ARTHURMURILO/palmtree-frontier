using System.Collections.Generic;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._PS.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.Components;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
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
    public async Task HeatPumpConnectsToThePipesItFacesWhenTurned()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();
            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            EntityUid west = default;
            EntityUid east = default;

            await server.WaitPost(() =>
            {
                // The test map is a single tile; the pipes need tiles of their own, both to stand on and
                // for the grid to cover them, or they cannot be anchored.
                var mapSys = server.System<SharedMapSystem>();
                var gridComp = entMan.GetComponent<MapGridComponent>(testMap.Grid.Owner);
                var tile = testMap.Tile.Tile;
                mapSys.SetTile(testMap.Grid.Owner, gridComp, new Vector2i(-1, 0), tile);
                mapSys.SetTile(testMap.Grid.Owner, gridComp, new Vector2i(1, 0), tile);

                // A pipe run going east to west. Straight pipes are longitudinal, so the pipes and the
                // machine all get the same quarter turn a player would give them with R.
                west = entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(-1f, 0f)));
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                east = entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(1f, 0f)));

                var quarterTurn = new Angle(Math.PI / 2);
                var westXform = entMan.GetComponent<TransformComponent>(west);
                var eastXform = entMan.GetComponent<TransformComponent>(east);
                var deviceXform = entMan.GetComponent<TransformComponent>(device);

                // Pipe nodes only join a network while the entity is anchored.
                westXform.Anchored = true;
                eastXform.Anchored = true;

                // Straight pipes are longitudinal, so they get the same quarter turn as the machine.
                westXform.LocalRotation = quarterTurn;
                eastXform.LocalRotation = quarterTurn;
                deviceXform.LocalRotation = quarterTurn;
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var gas = (PipeNode) entMan.GetComponent<NodeContainerComponent>(device).Nodes["gas"];
                var waste = (PipeNode) entMan.GetComponent<NodeContainerComponent>(device).Nodes["waste"];
                var westNode = (PipeNode) entMan.GetComponent<NodeContainerComponent>(west).Nodes["pipe"];
                var eastNode = (PipeNode) entMan.GetComponent<NodeContainerComponent>(east).Nodes["pipe"];

                Assert.Multiple(() =>
                {
                    Assert.That(gas.CurrentPipeDirection, Is.EqualTo(PipeDirection.East),
                        "the gas port should face east after a quarter turn");
                    Assert.That(waste.CurrentPipeDirection, Is.EqualTo(PipeDirection.West),
                        "the waste port should face west after a quarter turn");
                    Assert.That(gas.NodeGroup, Is.SameAs(eastNode.NodeGroup),
                        "the gas port should join the pipe it faces");
                    Assert.That(waste.NodeGroup, Is.SameAs(westNode.NodeGroup),
                        "the waste port should join the pipe it faces");
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task HeatPumpFollowsThePipeLayerItIsSetTo()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();
            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            EntityUid east = default;

            await server.WaitPost(() =>
            {
                var mapSys = server.System<SharedMapSystem>();
                var gridComp = entMan.GetComponent<MapGridComponent>(testMap.Grid.Owner);
                var tile = testMap.Tile.Tile;
                mapSys.SetTile(testMap.Grid.Owner, gridComp, new Vector2i(-1, 0), tile);
                mapSys.SetTile(testMap.Grid.Owner, gridComp, new Vector2i(1, 0), tile);

                // The whole run is on the second pipe layer; the machine starts on the first.
                var westPipe = entMan.SpawnEntity("GasPipeStraightAlt1", coords.Offset(new Vector2(-1f, 0f)));
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                east = entMan.SpawnEntity("GasPipeStraightAlt1", coords.Offset(new Vector2(1f, 0f)));

                var quarterTurn = new Angle(Math.PI / 2);
                foreach (var uid in new[] { westPipe, east })
                {
                    var xf = entMan.GetComponent<TransformComponent>(uid);
                    xf.Anchored = true;
                    xf.LocalRotation = quarterTurn;
                }

                entMan.GetComponent<TransformComponent>(device).LocalRotation = quarterTurn;
            });

            await pair.RunTicksSync(10);

            await server.WaitAssertion(() =>
            {
                var gas = (PipeNode) entMan.GetComponent<NodeContainerComponent>(device).Nodes["gas"];
                var eastNode = (PipeNode) entMan.GetComponent<NodeContainerComponent>(east).Nodes["pipe"];

                Assert.That(gas.NodeGroup, Is.Not.SameAs(eastNode.NodeGroup),
                    "a machine on the primary layer should not join a second layer pipe");
            });

            await server.WaitPost(() =>
            {
                var layers = entMan.GetComponent<AtmosPipeLayersComponent>(device);
                server.System<AtmosPipeLayersSystem>().SetPipeLayer(new Entity<AtmosPipeLayersComponent>(device, layers), AtmosPipeLayer.Secondary);
            });

            await pair.RunTicksSync(10);

            await server.WaitAssertion(() =>
            {
                var gas = (PipeNode) entMan.GetComponent<NodeContainerComponent>(device).Nodes["gas"];
                var eastNode = (PipeNode) entMan.GetComponent<NodeContainerComponent>(east).Nodes["pipe"];

                Assert.Multiple(() =>
                {
                    Assert.That(gas.CurrentPipeLayer, Is.EqualTo(AtmosPipeLayer.Secondary));
                    Assert.That(gas.NodeGroup, Is.SameAs(eastNode.NodeGroup),
                        "the machine should follow the run onto the second layer");
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

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
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 300f;
                pump.MotorHeatDeadband = 1000f;
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
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 500f;
                pump.MotorHeatDeadband = 1000f;
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
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
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
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
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
                waste.SetMoles(Gas.Oxygen, 2f);
                waste.Temperature = 300f;

                Raise(entMan, device);
                emptyEfficiency = entMan.GetComponent<GasHeatPumpComponent>(device).Efficiency;
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(fullEfficiency, Is.GreaterThan(0.5f));
                Assert.That(emptyEfficiency, Is.LessThan(fullEfficiency / 2f),
                    "A waste loop with almost no gas in it must strangle the machine.");
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
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
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
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
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

    /// <summary>
    /// The safeties have to be reachable: a gas mixture cannot go above `Atmospherics.Tmax`, so a
    /// threshold above it would never fire.
    /// </summary>
    [Test]
    public async Task HeatPumpSafetiesShutTheMachineDown()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 250f;
                pump.Safeties = true;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                var (gas, waste) = GetAir(entMan, device);
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 300f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = pump.SafeTemperature + 1000f;
            });

            await server.WaitAssertion(() => Raise(entMan, device));

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);

                Assert.That(pump.SafeTemperature, Is.LessThan(Atmospherics.Tmax),
                    "A threshold above Tmax can never be reached.");
                Assert.That(pump.Enabled, Is.False, "The safeties should switch the machine off.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// A huge temperature difference must not move an unbounded amount of heat per second.
    /// </summary>
    [Test]
    public async Task HeatPumpRespectsItsMaximumHeatRate()
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
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 100f;
                pump.MotorHeatDeadband = 1000f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                var (gasPort, waste) = GetAir(entMan, device);
                gas = gasPort;
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = Atmospherics.T20C;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = 300f;
            });

            await server.WaitAssertion(() => Raise(entMan, device));

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);

                Assert.That(pump.HeatMoved, Is.Negative, "The machine should be cooling.");
                Assert.That(MathF.Abs(pump.HeatMoved), Is.LessThanOrEqualTo(pump.MaxHeatRate + 1f),
                    "The heat rate must be capped.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// With the safeties off the machine stops caring about the waste loop, and the loop eventually
    /// takes it with it. The roll is probabilistic, so the test gives it enough tries that a failure
    /// would mean the path is unreachable.
    /// </summary>
    [Test]
    public async Task HeatPumpDetonatesWithTheSafetiesOff()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 250f;
                pump.Safeties = false;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                var (gas, waste) = GetAir(entMan, device);
                gas.SetMoles(Gas.Oxygen, 100f);
                gas.Temperature = 300f;
                waste.SetMoles(Gas.Oxygen, 100f);
                waste.Temperature = pump.SafeTemperature + pump.DetonationMargin + 1000f;
            });

            var destroyed = false;

            for (var attempt = 0; attempt < 30 && !destroyed; attempt++)
            {
                await server.WaitAssertion(() =>
                {
                    // The roll is throttled to once a second; skip the wait.
                    entMan.GetComponent<GasHeatPumpComponent>(device).NextDetonationCheck = TimeSpan.Zero;
                    Raise(entMan, device);
                });

                await pair.RunTicksSync(1);
                await server.WaitAssertion(() => destroyed = !entMan.EntityExists(device));
            }

            await server.WaitAssertion(() =>
            {
                Assert.That(destroyed, Is.True,
                    "A machine with the safeties off should eventually blow up on a hot waste loop.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// The machine has to react to its parts, and the bin rating has to raise the ceiling on the
    /// transfer as well as the heat capacity: the capacity alone is swamped by the gas's own, so
    /// without the ceiling a fully upgraded machine would move the same heat as a stock one.
    /// </summary>
    [Test]
    public async Task HeatPumpUpgradesWithItsParts()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;

            await server.WaitPost(() =>
            {
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);

                Assert.Multiple(() =>
                {
                    Assert.That(pump.HeatCapacity, Is.EqualTo(pump.BaseHeatCapacity),
                        "a machine with no parts should start on the stock heat capacity");
                    Assert.That(pump.MaxHeatRate, Is.EqualTo(pump.BaseMaxHeatRate),
                        "a machine with no parts should start on the stock heat rate");
                    Assert.That(pump.EfficiencyFloor, Is.EqualTo(pump.BaseEfficiencyFloor),
                        "a machine with no parts should start on the stock efficiency floor");
                });

                entMan.EventBus.RaiseLocalEvent(device, new RefreshPartsEvent
                {
                    PartRatings = new Dictionary<string, float>
                    {
                        [pump.MachinePartHeatCapacity] = 3f,
                        [pump.MachinePartEfficiency] = 4f,
                    },
                }, true);

                Assert.Multiple(() =>
                {
                    Assert.That(pump.HeatCapacity, Is.EqualTo(pump.BaseHeatCapacity * 4f).Within(1f),
                        "a tier three matter bin should quadruple the heat capacity");
                    Assert.That(pump.MaxHeatRate, Is.EqualTo(pump.BaseMaxHeatRate * 4f).Within(1f),
                        "a tier three matter bin should quadruple the most heat the machine can move");
                    Assert.That(pump.EfficiencyFloor, Is.EqualTo(pump.MaxEfficiencyFloor).Within(0.001f),
                        "tier four manipulators should reach the efficiency floor ceiling");
                    Assert.That(pump.PowerPerHeat, Is.EqualTo(pump.BasePowerPerHeat * 0.49f).Within(0.0001f),
                        "a tier three matter bin should make the compressor correspondingly more efficient");
                    Assert.That(pump.MinTemperature, Is.EqualTo(28.15f).Within(0.01f),
                        "tier four manipulators should let the machine be set 45 K colder");
                    Assert.That(pump.MaxTemperature, Is.EqualTo(1193.15f).Within(0.01f),
                        "tier four manipulators should let the machine be set 600 K hotter");
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// And the parts have to show up in the thing the player watches: the same gas and the same
    /// temperatures have to move more heat once the bins are better, not just change a number on the
    /// component.
    /// </summary>
    [Test]
    public async Task HeatPumpMovesMoreHeatWithBetterBins()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasThermoMachineHeatPump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            var stock = 0f;
            var stockPower = 0f;
            var upgraded = 0f;
            var maxHeat = 0f;
            var maxLoad = 0f;

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                pump.Enabled = true;
                pump.TargetTemperature = 300f;
                pump.MotorHeatDeadband = 1000f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                SetGas(entMan, device, 500f, 300f);
                Raise(entMan, device);
                stock = MathF.Abs(pump.HeatMoved);
                stockPower = entMan.GetComponent<ApcPowerReceiverComponent>(device).Load;
            });

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                SetGas(entMan, device, 500f, 300f);

                entMan.EventBus.RaiseLocalEvent(device, new RefreshPartsEvent
                {
                    PartRatings = new Dictionary<string, float>
                    {
                        [pump.MachinePartHeatCapacity] = 4f,
                    },
                }, true);

                Raise(entMan, device);
                upgraded = MathF.Abs(pump.HeatMoved);
            });

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasHeatPumpComponent>(device);
                // Big enough a difference that the machine is running into its 624 kW ceiling: the
                // exchange term is limited by the gas's own heat capacity, so a smaller delta would
                // never reach the cap.
                SetGas(entMan, device, 950f, 300f);

                Raise(entMan, device);
                maxHeat = MathF.Abs(pump.HeatMoved);
                maxLoad = entMan.GetComponent<ApcPowerReceiverComponent>(device).Load;
            });

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(stock, Is.GreaterThan(60_000f).And.LessThanOrEqualTo(78_000f),
                        "a stock machine should be running into its 78 kW ceiling");
                    Assert.That(upgraded, Is.GreaterThan(stock * 2f),
                        "better bins should raise the ceiling and let the machine move much more heat");
                    Assert.That(stock / stockPower, Is.GreaterThan(3.9f),
                        "the heat pump should be more efficient per joule than a thermomachine");
                    Assert.That(maxHeat, Is.GreaterThan(500_000f),
                        "a tier four machine should be working near its 624 kW ceiling");
                    Assert.That(maxLoad, Is.LessThan(55_000f),
                        "a fully upgraded machine should draw about 50 kW at full tilt, not 140");
                });
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

    private static void SetGas(IEntityManager entMan, EntityUid device, float gasTemperature, float wasteTemperature)
    {
        var (gas, waste) = GetAir(entMan, device);
        gas.SetMoles(Gas.Oxygen, 100f);
        gas.Temperature = gasTemperature;
        waste.SetMoles(Gas.Oxygen, 100f);
        waste.Temperature = wasteTemperature;
    }

    private static void Raise(IEntityManager entMan, EntityUid device)
    {
        var ev = new AtmosDeviceUpdateEvent(1f, null, null);
        entMan.EventBus.RaiseLocalEvent(device, ref ev);
    }
}
