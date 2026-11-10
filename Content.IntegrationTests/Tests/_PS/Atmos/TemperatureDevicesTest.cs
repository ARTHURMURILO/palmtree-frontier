using System.Numerics;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._PS.Atmos;
using Content.Shared._PS.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._PS.Atmos;

/// <summary>
/// Palmtree: behaviour tests for the temperature devices ported from /tg/station.
/// </summary>
/// <remarks>
/// The tests build a tiny two-pipeline setup - a pipe, a device and a second pipe - seed the two
/// mixtures by hand and then raise <see cref="AtmosDeviceUpdateEvent"/> directly, so the device
/// logic is exercised without waiting for the atmos ticker.
/// </remarks>
[TestFixture]
public sealed class TemperatureDevicesTest
{
    private static readonly EntProtoId PipeProto = "GasPipeStraight";

    [Test]
    public async Task TemperatureGateOnlyOpensBelowThreshold()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture inletAir = default!;
            GasMixture outletAir = default!;

            await server.WaitPost(() =>
            {
                // The device's inlet faces north and its outlet faces south, so the north pipe feeds
                // the inlet and the south pipe is the outlet.
                var southPipe = entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasTemperatureGate", coords);
                var northPipe = entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));

                Assert.That(southPipe, Is.Not.EqualTo(EntityUid.Invalid));
                Assert.That(northPipe, Is.Not.EqualTo(EntityUid.Invalid));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var gate = entMan.GetComponent<GasTemperatureGateComponent>(device);
                gate.Enabled = true;
                gate.TargetTemperature = 273.15f;

                // The gate only runs when powered.
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                var (inlet, outlet) = GetNodes(entMan, device);
                inletAir = inlet.Air;
                outletAir = outlet.Air;

                inletAir.SetMoles(Gas.Oxygen, 100f);
                inletAir.Temperature = 200f;
                outletAir.SetMoles(Gas.Oxygen, 10f);
                outletAir.Temperature = 200f;
            });

            // Cold inlet: the condition is met, gas should move.
            var inletMolesBefore = 0f;
            var outletMolesBefore = 0f;

            await server.WaitAssertion(() =>
            {
                inletMolesBefore = inletAir.TotalMoles;
                outletMolesBefore = outletAir.TotalMoles;
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(inletAir.TotalMoles, Is.LessThan(inletMolesBefore), "Gas should leave the inlet.");
                Assert.That(outletAir.TotalMoles, Is.GreaterThan(outletMolesBefore), "Gas should reach the outlet.");
            });

            // Hot inlet: the condition is no longer met, the gate must stay shut.
            await server.WaitAssertion(() =>
            {
                inletAir.Temperature = 400f;
                inletMolesBefore = inletAir.TotalMoles;
                outletMolesBefore = outletAir.TotalMoles;
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(inletAir.TotalMoles, Is.EqualTo(inletMolesBefore).Within(0.01f), "Gas must not leave the inlet.");
                Assert.That(outletAir.TotalMoles, Is.EqualTo(outletMolesBefore).Within(0.01f), "Gas must not reach the outlet.");
            });

            // Inverting the gate flips the condition.
            await server.WaitAssertion(() =>
            {
                entMan.GetComponent<GasTemperatureGateComponent>(device).Inverted = true;
                inletMolesBefore = inletAir.TotalMoles;
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(inletAir.TotalMoles, Is.LessThan(inletMolesBefore), "An inverted gate should open on hot gas.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// An outlet with no pipe of its own is still a node group with its own empty mixture, so the gate
    /// equalises into it. What must never happen is gas going missing on the way.
    /// </summary>
    [Test]
    public async Task TemperatureGateKeepsGasWhenTheOutletHasNoPipe()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture inletAir = default!;
            GasMixture outletAir = default!;

            await server.WaitPost(() =>
            {
                // Only the inlet is piped in; the outlet is left hanging.
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
                device = entMan.SpawnEntity("GasTemperatureGate", coords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var gate = entMan.GetComponent<GasTemperatureGateComponent>(device);
                gate.Enabled = true;
                gate.TargetTemperature = 273.15f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                var (inlet, outlet) = GetNodes(entMan, device);
                inletAir = inlet.Air;
                outletAir = outlet.Air;

                inletAir.SetMoles(Gas.Oxygen, 100f);
                inletAir.Temperature = 200f;
            });

            var totalBefore = 0f;

            await server.WaitAssertion(() =>
            {
                totalBefore = inletAir.TotalMoles + outletAir.TotalMoles;
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(inletAir.TotalMoles + outletAir.TotalMoles, Is.EqualTo(totalBefore).Within(0.01f),
                    "Gas must not disappear when the outlet has nowhere to put it.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task TemperatureGateStaysShutWhileUnpowered()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture inletAir = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasTemperatureGate", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var gate = entMan.GetComponent<GasTemperatureGateComponent>(device);
                gate.Enabled = true;
                gate.TargetTemperature = 273.15f;

                var (inlet, _) = GetNodes(entMan, device);
                inletAir = inlet.Air;
                inletAir.SetMoles(Gas.Oxygen, 100f);
                inletAir.Temperature = 200f;
            });

            var molesBefore = 0f;

            await server.WaitAssertion(() =>
            {
                molesBefore = inletAir.TotalMoles;
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(inletAir.TotalMoles, Is.EqualTo(molesBefore).Within(0.01f),
                    "An unpowered gate must not move gas even when its condition is met.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task TemperaturePumpMovesHeatButNotGas()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture inletAir = default!;
            GasMixture outletAir = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasTemperaturePump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasTemperaturePumpComponent>(device);
                pump.Enabled = true;
                pump.HeatTransferRate = 100f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                var (inlet, outlet) = GetNodes(entMan, device);
                inletAir = inlet.Air;
                outletAir = outlet.Air;

                // Same gas and same amount on both sides, so equal temperature deltas mean equal energy.
                inletAir.SetMoles(Gas.Oxygen, 100f);
                inletAir.Temperature = 400f;
                outletAir.SetMoles(Gas.Oxygen, 100f);
                outletAir.Temperature = 200f;
            });

            var inletMoles = 0f;
            var outletMoles = 0f;

            await server.WaitAssertion(() =>
            {
                inletMoles = inletAir.TotalMoles;
                outletMoles = outletAir.TotalMoles;
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(inletAir.Temperature, Is.LessThan(400f), "The inlet should be cooled.");
                Assert.That(outletAir.Temperature, Is.GreaterThan(200f), "The outlet should be heated.");

                // The pump moves heat only.
                Assert.That(inletAir.TotalMoles, Is.EqualTo(inletMoles).Within(0.001f));
                Assert.That(outletAir.TotalMoles, Is.EqualTo(outletMoles).Within(0.001f));

                // Energy is conserved: whatever the inlet lost, the outlet gained.
                Assert.That(inletAir.Temperature + outletAir.Temperature, Is.EqualTo(600f).Within(0.5f));
            });

            // Cold inlet: heat must not travel backwards.
            await server.WaitAssertion(() =>
            {
                inletAir.Temperature = 150f;
                outletAir.Temperature = 300f;
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(inletAir.Temperature, Is.EqualTo(150f).Within(0.01f), "The pump must not cool the inlet below the outlet.");
                Assert.That(outletAir.Temperature, Is.EqualTo(300f).Within(0.01f));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// The pump walks both sides towards the heat-capacity weighted equilibrium, not the plain average
    /// of the two temperatures, and it must not create or destroy heat while doing so.
    /// </summary>
    [Test]
    public async Task TemperaturePumpWeighsByHeatCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            GasMixture inletAir = default!;
            GasMixture outletAir = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                device = entMan.SpawnEntity("GasTemperaturePump", coords);
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pump = entMan.GetComponent<GasTemperaturePumpComponent>(device);
                pump.Enabled = true;
                pump.HeatTransferRate = 100f;
                entMan.GetComponent<ApcPowerReceiverComponent>(device).Powered = true;

                var (inlet, outlet) = GetNodes(entMan, device);
                inletAir = inlet.Air;
                outletAir = outlet.Air;

                // Three times as much gas on the cold side, so the equilibrium sits at
                // (100*500 + 300*200) / 400 = 275 K, well below the 350 K a plain average would give.
                inletAir.SetMoles(Gas.Oxygen, 100f);
                inletAir.Temperature = 500f;
                outletAir.SetMoles(Gas.Oxygen, 300f);
                outletAir.Temperature = 200f;
            });

            await server.WaitAssertion(() =>
            {
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                // Weighted thermal energy in, weighted thermal energy out.
                var before = 100f * 500f + 300f * 200f;
                var after = 100f * inletAir.Temperature + 300f * outletAir.Temperature;

                Assert.That(after, Is.EqualTo(before).Within(1f), "The pump must not create or destroy heat.");
                Assert.That(inletAir.Temperature, Is.LessThan(400f),
                    "The hot side should fall below the midpoint of the two starting temperatures.");
                Assert.That(outletAir.Temperature, Is.LessThan(350f),
                    "The cold side should stay below the plain average, because it holds most of the mass.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task HeatExchangerPairEqualisesWithoutMovingGas()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            EntityUid partner = default;
            GasMixture air = default!;
            GasMixture partnerAir = default!;

            await server.WaitPost(() =>
            {
                (device, partner) = SpawnPair(server, coords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                air = GetNode(entMan, device).Air;
                partnerAir = GetNode(entMan, partner).Air;

                air.SetMoles(Gas.Oxygen, 100f);
                air.Temperature = 500f;
                partnerAir.SetMoles(Gas.Oxygen, 100f);
                partnerAir.Temperature = 300f;
            });

            await server.WaitAssertion(() =>
            {
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(air.Temperature, Is.EqualTo(400f).Within(1f), "The hot side should cool to the equilibrium.");
                Assert.That(partnerAir.Temperature, Is.EqualTo(400f).Within(1f), "The cold side should warm to the equilibrium.");

                // Equal moles and equal heat capacity, so nothing should have moved.
                Assert.That(air.TotalMoles, Is.EqualTo(100f).Within(0.01f));
                Assert.That(partnerAir.TotalMoles, Is.EqualTo(100f).Within(0.01f));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// The exchanger weighs by heat capacity, so a small hot pipeline and a large cold one meet well
    /// below the midpoint.
    /// </summary>
    [Test]
    public async Task HeatExchangerPairWeighsByHeatCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            EntityUid partner = default;
            GasMixture air = default!;
            GasMixture partnerAir = default!;

            await server.WaitPost(() =>
            {
                (device, partner) = SpawnPair(server, coords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                air = GetNode(entMan, device).Air;
                partnerAir = GetNode(entMan, partner).Air;

                air.SetMoles(Gas.Oxygen, 100f);
                air.Temperature = 500f;
                partnerAir.SetMoles(Gas.Oxygen, 300f);
                partnerAir.Temperature = 200f;
            });

            await server.WaitAssertion(() =>
            {
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                // (100*500 + 300*200) / 400 = 275 K.
                Assert.That(air.Temperature, Is.EqualTo(275f).Within(1f));
                Assert.That(partnerAir.Temperature, Is.EqualTo(275f).Within(1f));
                Assert.That(air.TotalMoles, Is.EqualTo(100f).Within(0.01f));
                Assert.That(partnerAir.TotalMoles, Is.EqualTo(300f).Within(0.01f));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// A heat exchanger only works with a second one facing it. A lone device, or two of them
    /// placed back to back, must not touch the gas at all.
    /// </summary>
    [Test]
    public async Task HeatExchangerDoesNothingWithoutAFacingPartner()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var loneCoords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));
            var backCoords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(3.5f, 0.5f));

            EntityUid lone = default;
            EntityUid back = default;
            EntityUid backPartner = default;
            GasMixture loneAir = default!;
            GasMixture backAir = default!;
            GasMixture backPartnerAir = default!;

            var backPartnerCoords = backCoords.Offset(new Vector2(0f, 1f));

            await server.WaitPost(() =>
            {
                EnsureTile(server, loneCoords);
                EnsureTile(server, backCoords);
                EnsureTile(server, backPartnerCoords);

                lone = entMan.SpawnEntity("GasHeatExchanger", loneCoords);

                // Two devices with their pipes pointing the same way: their faces do not touch.
                back = entMan.SpawnEntity("GasHeatExchanger", backCoords);
                backPartner = entMan.SpawnEntity("GasHeatExchanger", backPartnerCoords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                loneAir = GetNode(entMan, lone).Air;
                loneAir.SetMoles(Gas.Oxygen, 100f);
                loneAir.Temperature = 500f;

                backAir = GetNode(entMan, back).Air;
                backAir.SetMoles(Gas.Oxygen, 100f);
                backAir.Temperature = 500f;

                backPartnerAir = GetNode(entMan, backPartner).Air;
                backPartnerAir.SetMoles(Gas.Oxygen, 100f);
                backPartnerAir.Temperature = 300f;
            });

            await server.WaitAssertion(() =>
            {
                Raise(entMan, lone);
                Raise(entMan, back);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(loneAir.Temperature, Is.EqualTo(500f).Within(0.1f),
                    "A heat exchanger without a partner must leave its pipeline alone.");
                Assert.That(backAir.Temperature, Is.EqualTo(500f).Within(0.1f),
                    "Two heat exchangers that are not facing each other must not exchange heat.");
                Assert.That(backPartnerAir.Temperature, Is.EqualTo(300f).Within(0.1f));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task AtmosMeterReadsItsPipeline()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid device = default;
            PipeNode monitored = default!;

            await server.WaitPost(() =>
            {
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, -1f)));
                entMan.SpawnEntity(PipeProto, coords.Offset(new Vector2(0f, 1f)));
                device = entMan.SpawnEntity("GasMeter", coords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var container = entMan.GetComponent<NodeContainerComponent>(device);
                monitored = (PipeNode) container.Nodes["monitored"];

                // Roughly two atmospheres of warm gas.
                monitored.Air.SetMoles(Gas.Nitrogen, 200f);
                monitored.Air.Temperature = 350f;
            });

            await server.WaitAssertion(() =>
            {
                Raise(entMan, device);
            });

            await server.WaitAssertion(() =>
            {
                var meter = entMan.GetComponent<GasMeterComponent>(device);

                Assert.That(meter.Pressure, Is.GreaterThan(0f), "The meter should read a pressure.");
                Assert.That(meter.Temperature, Is.EqualTo(350f).Within(5f), "The meter should read the temperature.");
                Assert.That(meter.PressureLevel, Is.Not.EqualTo(GasMeterPressureLevel.Zero));
                Assert.That(meter.TemperatureLevel, Is.EqualTo(GasMeterTemperatureLevel.Warm));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static (PipeNode inlet, PipeNode outlet) GetNodes(IEntityManager entMan, EntityUid device)
    {
        var container = entMan.GetComponent<NodeContainerComponent>(device);

        return ((PipeNode) container.Nodes["inlet"], (PipeNode) container.Nodes["outlet"]);
    }

    private static PipeNode GetNode(IEntityManager entMan, EntityUid device)
    {
        var container = entMan.GetComponent<NodeContainerComponent>(device);

        return (PipeNode) container.Nodes["pipe"];
    }

    /// <summary>
    /// Spawns two heat exchangers with their flat faces touching and anchors both of them.
    /// </summary>
    /// <summary>
    /// The test map only has a single plating tile; put one under everything that has to be
    /// anchored.
    /// </summary>
    private static void EnsureTile(RobustIntegrationTest.ServerIntegrationInstance server, EntityCoordinates coords)
    {
        var entMan = server.EntMan;
        var map = server.System<SharedMapSystem>();
        var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);

        var gridUid = coords.EntityId;
        var grid = entMan.GetComponent<MapGridComponent>(gridUid);
        var indices = map.TileIndicesFor(gridUid, grid, coords);

        map.SetTile(gridUid, grid, indices, plating);
    }

    private static (EntityUid device, EntityUid partner) SpawnPair(
        RobustIntegrationTest.ServerIntegrationInstance server,
        EntityCoordinates coords)
    {
        var entMan = server.EntMan;
        var xform = server.System<SharedTransformSystem>();

        var partnerCoords = coords.Offset(new Vector2(0f, 1f));
        EnsureTile(server, coords);
        EnsureTile(server, partnerCoords);

        var device = entMan.SpawnEntity("GasHeatExchanger", coords);
        var partner = entMan.SpawnEntity("GasHeatExchanger", partnerCoords);

        // Turn the partner around: its flat face now looks down at the first device, and the two
        // pipe stubs point away from each other.
        xform.SetLocalRotation(partner, Direction.North.ToAngle());

        return (device, partner);
    }

    private static void Raise(IEntityManager entMan, EntityUid device)
    {
        var ev = new AtmosDeviceUpdateEvent(0.5f, null, null);
        entMan.EventBus.RaiseLocalEvent(device, ref ev);
    }
}
