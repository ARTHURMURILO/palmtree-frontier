using System.Linq;
using Content.Server._EE.Supermatter.Components;
using Content.Server._EE.Supermatter.Console.Systems;
using Content.Server._PS.BluespaceDock;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Server.Tesla.Components;
using Content.Shared._EE.Supermatter.Components;
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
    public async Task InertCrystalDoesNotRadiateUntilActivated()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            atmos.SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));
        });

        EntityUid sm = default;

        await server.WaitPost(() => sm = entMan.SpawnEntity("Supermatter", map.GridCoords));

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var rad = entMan.GetComponent<RadiationSourceComponent>(sm);
            Assert.That(rad.Intensity, Is.EqualTo(0f),
                "an inert supermatter must not emit radiation");
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            comp.Activated = true;
            comp.Power = 1000f;
        });

        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var rad = entMan.GetComponent<RadiationSourceComponent>(sm);
            Assert.That(rad.Intensity, Is.GreaterThan(0f),
                "an active supermatter must emit radiation");
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SupermatterComponent>(sm);
            comp.Activated = false;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var rad = entMan.GetComponent<RadiationSourceComponent>(sm);
            Assert.That(rad.Intensity, Is.EqualTo(0f),
                "deactivating the crystal must stop its radiation");
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
