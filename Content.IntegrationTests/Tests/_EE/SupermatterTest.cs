using System.Linq;
using Content.Server._EE.Supermatter.Components;
using Content.Server._EE.Supermatter.Console.Systems;
using Content.Server._EE.Supermatter.Systems;
using Content.Server._PS.BluespaceDock;
using Content.Server.Anomaly.Components;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Server.Singularity.EntitySystems;
using Content.Server.Tesla.Components;
using Content.Shared._EE.Supermatter.Components;
using Content.Shared.Anomaly.Components;
using Content.Shared.Atmos;
using Content.Shared._EE.Supermatter.Consoles;
using Content.Shared.Construction.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Radiation.Components;
using Content.Shared.Radio.Components;
using Content.Shared.Singularity.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._EE;

[TestFixture]
public sealed class SupermatterTest
{
    [Test]
    public async Task ActiveCrystalChargesNearbyTeslaCoil()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        EntityUid sm = default;
        EntityUid coil = default;

        await server.WaitPost(() =>
        {
            sm = entMan.SpawnEntity("Supermatter", map.GridCoords);
            coil = entMan.SpawnEntity("TeslaCoil", new EntityCoordinates(map.Grid, 1, 0));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<RadiationSourceComponent>(sm), Is.True,
                "the supermatter must be radioactive");
            Assert.That(entMan.GetComponent<RadiationSourceComponent>(sm).Intensity, Is.EqualTo(0f),
                "an inert crystal must not radiate");
            var battery = entMan.GetComponent<BatteryComponent>(coil);
            Assert.That(battery.CurrentCharge, Is.EqualTo(0f),
                "an inert crystal must not charge a coil");
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            comp.Activated = true;
            comp.Power = 5000f;
        });

        await pair.RunTicksSync(65);

        await server.WaitAssertion(() =>
        {
            var battery = entMan.GetComponent<BatteryComponent>(coil);
            Assert.That(battery.CurrentCharge, Is.GreaterThan(0f),
                "an active crystal must arc into a nearby tesla coil");
            Assert.That(entMan.GetComponent<RadiationSourceComponent>(sm).Intensity, Is.GreaterThan(0f),
                "an active crystal must be radioactive");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AmmoniaMiasmaIsConsumedLikeSS13()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();

        var map = await pair.CreateTestMap();
        EntityUid sm = default;

        await server.WaitPost(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Ammonia] = 100f;
            moles[(int) Gas.Nitrogen] = 1000f;

            if (!entMan.HasComponent<MapAtmosphereComponent>(map.MapUid))
                entMan.AddComponent<MapAtmosphereComponent>(map.MapUid);

            // The map-atmosphere setter deliberately freezes its mixture. The test needs a
            // mutable mixture because SS13-style gas handling removes and merges the sample.
            var mapAtmos = entMan.GetComponent<MapAtmosphereComponent>(map.MapUid);
            typeof(MapAtmosphereComponent)
                .GetField(nameof(MapAtmosphereComponent.Mixture))!
                .SetValue(mapAtmos, new GasMixture(moles, Atmospherics.T20C));

            sm = entMan.SpawnEntity("Supermatter", map.GridCoords);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            comp.Activated = true;
            comp.Power = 5000f;
        });

        var initialAmmonia = 0f;
        await server.WaitAssertion(() =>
        {
            var mix = atmos.GetContainingMixture(sm, true, true);
            Assert.That(mix, Is.Not.Null);
            initialAmmonia = mix!.GetMoles(Gas.Ammonia);
            Assert.That(initialAmmonia, Is.GreaterThan(0f));
        });

        await pair.RunTicksSync(65);

        await server.WaitAssertion(() =>
        {
            var mix = atmos.GetContainingMixture(sm, true, true);
            Assert.That(mix, Is.Not.Null);
            Assert.That(mix!.GetMoles(Gas.Ammonia), Is.LessThan(initialAmmonia),
                "the crystal must consume ammonia/miasma like SS13");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GasMixModifiesLightningTransmission()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Plasma] = 1000f;
            atmos.SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));
        });

        EntityUid sm = default;

        await server.WaitPost(() => sm = entMan.SpawnEntity("Supermatter", map.GridCoords));
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            comp.Activated = true;
            comp.Power = 5000f;
        });

        await pair.RunTicksSync(65);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            Assert.That(comp.LightningTransmission, Is.GreaterThan(1.1f),
                "plasma must increase lightning power transmission");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SupermatterRadiationDoesNotChargeCollectors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var collectorSystem = server.System<RadiationCollectorSystem>();

        var map = await pair.CreateTestMap();
        EntityUid sm = default;
        EntityUid collector = default;

        await server.WaitPost(() =>
        {
            sm = entMan.SpawnEntity("Supermatter", map.GridCoords);
            collector = entMan.SpawnEntity("RadiationCollectorFullTank", new EntityCoordinates(map.Grid, 1, 0));
            collectorSystem.SetCollectorEnabled(collector, true);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            comp.Activated = true;
            comp.Power = 5000f;
        });

        await pair.RunTicksSync(65);

        await server.WaitAssertion(() =>
        {
            var rad = entMan.GetComponent<RadiationSourceComponent>(sm);
            Assert.That(rad.Intensity, Is.GreaterThan(0f),
                "the active crystal must be radioactive");

            var supplier = entMan.GetComponent<PowerSupplierComponent>(collector);
            Assert.That(supplier.MaxSupply, Is.EqualTo(0f),
                "supermatter radiation must never charge a radiation collector");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DelaminationRadiusScalesWithInternalEnergy()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var system = server.System<SupermatterSystem>();

        var map = await pair.CreateTestMap();
        EntityUid sm = default;

        await server.WaitPost(() => sm = entMan.SpawnEntity("Supermatter", map.GridCoords));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);

            comp.Power = 0f;
            Assert.That(system.GetDelaminationRadius(comp), Is.EqualTo(20f));

            comp.Power = 3999f;
            Assert.That(system.GetDelaminationRadius(comp), Is.EqualTo(20f));

            comp.Power = 4000f;
            Assert.That(system.GetDelaminationRadius(comp), Is.EqualTo(21f));

            comp.Power = 8000f;
            Assert.That(system.GetDelaminationRadius(comp), Is.EqualTo(22f));

            comp.Power = 20000f;
            Assert.That(system.GetDelaminationRadius(comp), Is.EqualTo(25f));

            Assert.That(system.GetDelaminationAnomalyCount(comp), Is.EqualTo(6),
                "maximum severity must spawn six hyperflux anomalies");
        });

        EntityUid shard = default;
        await server.WaitPost(() => shard = entMan.SpawnEntity("SupermatterSmall", map.GridCoords));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(shard);
            comp.Power = 0f;
            Assert.That(system.GetDelaminationRadius(comp), Is.EqualTo(7f).Within(0.01f),
                "the shard must have a much smaller blast than the full crystal");

            Assert.That(system.GetDelaminationAnomalyCount(comp), Is.EqualTo(1),
                "a cold delamination must spawn one hyperflux anomaly");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DelaminationIsExplosionOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();

        var map = await pair.CreateTestMap();
        EntityUid sm = default;

        await server.WaitPost(() => sm = entMan.SpawnEntity("Supermatter", map.GridCoords));

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            comp.Activated = true;
            comp.Damage = comp.DamageDelaminationPoint;
            comp.Delamming = true;
            comp.DelamEndTime = timing.CurTime;
        });

        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Count<SingularityComponent>(), Is.EqualTo(0),
                "supermatter delamination must not spawn a singularity");
            Assert.That(entMan.Count<TeslaEnergyBallComponent>(), Is.EqualTo(0),
                "supermatter delamination must not spawn a full tesla ball");
            Assert.That(entMan.Count<SupermatterComponent>(), Is.EqualTo(0),
                "the exploding crystal should be deleted");
            Assert.That(entMan.Count<SupermatterZapBallComponent>(), Is.GreaterThan(0),
                "the explosion should leave short-lived supermatter discharge balls");
        });

        await pair.RunTicksSync(65);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Count<AnomalyComponent>(), Is.GreaterThan(0),
                "delamination should scatter hyperflux anomalies");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DangerousEnginesBlockShipStorageAndCanBePurged()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var dock = server.System<BluespaceDockSystem>();

        var map = await pair.CreateTestMap();
        EntityUid sm = default;

        await server.WaitPost(() => sm = entMan.SpawnEntity("Supermatter", map.GridCoords));

        await server.WaitAssertion(() =>
        {
            Assert.That(dock.HasDangerousShipEntities(map.Grid), Is.True,
                "a supermatter must block ship storage");

            var path = BluespaceDockSystem.GetShipFilePath("supermatter-save-block-test");
            Assert.That(dock.TrySaveShipFile(map.Grid.Owner, path, out var error), Is.False,
                "a ship with a supermatter must not be saved");
            Assert.That(error, Is.EqualTo("bluespace-dock-error-dangerous-engine-aboard"));

            Assert.That(dock.PurgeDangerousShipEntities(map.Grid), Is.GreaterThan(0));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(sm), Is.False, "the supermatter should have been purged");
            Assert.That(dock.HasDangerousShipEntities(map.Grid), Is.False,
                "the grid should no longer contain a dangerous engine");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HyperfluxAnomalyIsScienceNeutralizable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        EntityUid anomaly = default;

        await server.WaitPost(() => anomaly = entMan.SpawnEntity("AnomalyHyperflux", map.GridCoords));
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<AnomalyComponent>(anomaly), Is.True,
                "hyperflux must use the normal anomaly component");
            Assert.That(entMan.HasComponent<SecretDataAnomalyComponent>(anomaly), Is.True,
                "hyperflux must expose secret data to anomaly scanners");
            Assert.That(entMan.GetComponent<AnomalyComponent>(anomaly).CorePrototype, Is.Not.Null,
                "hyperflux must have a core prototype so it can be neutralized like any anomaly");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShardIsMovableAndAccessoriesExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        var map = await pair.CreateTestMap();
        EntityUid shard = default;

        await server.WaitPost(() => shard = entMan.SpawnEntity("SupermatterSmall", map.GridCoords));

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(shard).Anchored, Is.False);
            Assert.That(entMan.HasComponent<AnchorableComponent>(shard), Is.True);
            Assert.That(entMan.HasComponent<PullableComponent>(shard), Is.True);
            Assert.That(entMan.GetComponent<PhysicsComponent>(shard).BodyType,
                Is.EqualTo(Robust.Shared.Physics.BodyType.Dynamic));

            foreach (var id in new[]
                     {
                         "SupermatterSmallFlatpack",
                         "SupermatterSliver",
                         "ComputerSupermatter",
                         "SupermatterZapBall",
                     })
            {
                Assert.That(protoMan.HasIndex<EntityPrototype>(id), Is.True, $"missing prototype {id}");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MonitoringConsoleAndCrystalSelfInspection()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();

        var map = await pair.CreateTestMap();
        EntityUid console = default;
        EntityUid crystal = default;
        EntityUid actor = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"];
            var tile = new Tile(plating.TileId);

            for (var x = 0; x < 3; x++)
                mapSystem.SetTile(map.Grid, new EntityCoordinates(map.Grid, x, 0), tile);

            console = entMan.SpawnEntity("ComputerSupermatter", new EntityCoordinates(map.Grid, 0, 0));
            actor = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1, 0));
            crystal = entMan.SpawnEntity("Supermatter", new EntityCoordinates(map.Grid, 2, 0));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryOpenUi(console, SupermatterConsoleUiKey.Key, actor), Is.True);
            Assert.That(ui.TryOpenUi(crystal, SupermatterConsoleUiKey.Key, actor), Is.True);
        });

        await pair.RunTicksSync(65);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<SupermatterConsoleBoundInterfaceState>(
                    console, SupermatterConsoleUiKey.Key, out var consoleState), Is.True);
            Assert.That(consoleState, Is.Not.Null);
            Assert.That(consoleState!.Supermatters.Select(entry => entry.NetEntity),
                Is.EquivalentTo(new[] { entMan.GetNetEntity(crystal) }));

            Assert.That(ui.TryGetUiState<SupermatterConsoleBoundInterfaceState>(
                    crystal, SupermatterConsoleUiKey.Key, out var selfState), Is.True);
            Assert.That(selfState, Is.Not.Null);
            Assert.That(selfState!.SelfInspection, Is.True);
            Assert.That(selfState.Supermatters, Has.Length.EqualTo(1));
            Assert.That(selfState.Supermatters[0].NetEntity,
                Is.EqualTo(entMan.GetNetEntity(crystal)));
            Assert.That(selfState.FocusData, Is.Not.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SupermatterAlarmsUseAMonitoringConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var consoleSystem = server.System<SupermatterConsoleSystem>();

        var map = await pair.CreateTestMap();
        EntityUid crystal = default;
        EntityUid consoleA = default;
        EntityUid consoleB = default;

        await server.WaitPost(() =>
        {
            crystal = entMan.SpawnEntity("Supermatter", map.GridCoords);
            consoleA = entMan.SpawnEntity("ComputerSupermatter", map.GridCoords);
            consoleB = entMan.SpawnEntity("ComputerSupermatter", map.GridCoords);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<TelecomExemptComponent>(crystal), Is.False,
                "the crystal must not be able to transmit on the Shortband itself");
            Assert.That(entMan.HasComponent<TelecomExemptComponent>(consoleA), Is.True,
                "the console must be able to transmit without a telecom server");
            Assert.That(entMan.HasComponent<RadioMicrophoneComponent>(consoleA), Is.False,
                "the console must not relay or repeat Shortband traffic");
            Assert.That(entMan.HasComponent<RadioSpeakerComponent>(consoleA), Is.False,
                "the console must not emit received radio traffic");
            Assert.That(entMan.HasComponent<ActiveRadioComponent>(consoleA), Is.False,
                "the console must not listen to radio channels");
            Assert.That(entMan.HasComponent<IntrinsicRadioReceiverComponent>(consoleA), Is.False,
                "the console must not listen to radio channels");

            var powerA = entMan.GetComponent<ApcPowerReceiverComponent>(consoleA);
            var powerB = entMan.GetComponent<ApcPowerReceiverComponent>(consoleB);

            powerA.Powered = true;
            powerB.Powered = false;
            Assert.That(consoleSystem.TryGetAnnouncingConsole(crystal, out var console), Is.True);
            Assert.That(console, Is.EqualTo(consoleA),
                "an unpowered console must not be picked while a powered one exists");

            powerA.Powered = false;
            powerB.Powered = true;
            Assert.That(consoleSystem.TryGetAnnouncingConsole(crystal, out console), Is.True);
            Assert.That(console, Is.EqualTo(consoleB),
                "the powered console must be picked regardless of its order");

            powerA.Powered = true;
            powerB.Powered = true;
            Assert.That(consoleSystem.TryGetAnnouncingConsole(crystal, out console), Is.True);
            Assert.That(console, Is.EqualTo(consoleA).Or.EqualTo(consoleB),
                "alarms must be broadcast from one of the powered monitoring consoles, never the crystal");

            powerA.Powered = false;
            powerB.Powered = false;
            Assert.That(consoleSystem.TryGetAnnouncingConsole(crystal, out _), Is.False,
                "unpowered consoles must not broadcast");
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(consoleA);
            entMan.DeleteEntity(consoleB);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(consoleSystem.TryGetAnnouncingConsole(crystal, out _), Is.False,
                "a crystal without a monitoring console must not broadcast");
        });

        await pair.CleanReturnAsync();
    }
}
