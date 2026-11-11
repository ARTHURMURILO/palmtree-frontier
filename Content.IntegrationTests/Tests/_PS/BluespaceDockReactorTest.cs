using System;
using System.Numerics;
using Content.Server._NSV13.Reactor.Components;
using Content.Server._PS.BluespaceDock;
using Content.Shared.Audio;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the bluespace dock must not store a live reactor.
/// </summary>
/// <remarks>
/// A reactor's running state and temperature are serialized, but the gas behind it is not. A ship
/// stored mid-reaction therefore reloads live with empty pipes: it starves, eats its own vessel
/// integrity, and can melt down with nobody aboard. The gate refuses the store, and the admin
/// force-dock path may only bypass it by safing the cores first (<c>--scram</c>).
/// </remarks>
[TestFixture]
public sealed class BluespaceDockReactorTest
{
    [Test]
    public async Task RunningReactorBlocksStorageUntilSafed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var rbmk = entMan.GetComponent<RbmkReactorComponent>(reactor);

            // A cold, stopped reactor is ordinary cargo.
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a cold stopped reactor should be storable");

            // A running core is not.
            rbmk.IsRunning = true;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(reactor), "a running reactor must block the store");

            // The scram path is the admin's explicit way out, and it leaves the core stopped.
            Assert.That(dock.SafeReactors(grid), Is.EqualTo(1), "the scram should report the reactor it safed");
            Assert.That(rbmk.IsRunning, Is.False, "a safed reactor must be stopped");
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a safed reactor should be storable");

            // Hot but stopped is still refused: the temperature is serialized, and it would come
            // back hot with nothing to cool it.
            rbmk.Temperature = 400f;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(reactor), "a hot reactor must block the store");

            dock.SafeReactors(grid);
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "the scram should cool it below the threshold");

            // A slagged wreck is legitimate cargo and must not be blocked by the reactor rule.
            rbmk.Slagged = true;
            rbmk.Temperature = 900f;
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a slagged wreck is cargo, not a live core");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RunningStormdriveBlocksStorageUntilSafed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var drive = entMan.SpawnEntity("StormdriveReactor", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "an idle drive should be storable");

            comp.State = StormdriveState.Running;
            comp.Heat = 300f;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(drive), "a running drive must block the store");

            // An armed fuse is refused even if the drive itself looks idle.
            comp.State = StormdriveState.Idle;
            comp.Heat = 0f;
            comp.ReactorEndTimes = true;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(drive), "an armed fuse must block the store");

            comp.ReactorEndTimes = false;

            // The scram cancels the fuse and drops the heat.
            comp.State = StormdriveState.Running;
            comp.Heat = 300f;
            Assert.That(dock.SafeReactors(grid), Is.EqualTo(1));
            Assert.That(comp.ReactorEndTimes, Is.False, "the scram must cancel an armed fuse");
            Assert.That(comp.Heat, Is.LessThan(50f), "the scram must bring the drive below the storage threshold");
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a safed drive should be storable");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The threshold is exclusive: a core below 50 °C may be stored, one at exactly 50 °C may not.
    /// The locale message says "cool below 50 °C", and the gate uses <c>&gt;= 50</c> for both engines,
    /// so the two must agree.
    /// </summary>
    [Test]
    public async Task TemperatureExactlyAtTheThresholdBlocksStorage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var rbmk = entMan.GetComponent<RbmkReactorComponent>(reactor);

            rbmk.Temperature = 49.9f;
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "49.9 °C is below the threshold and storable");

            rbmk.Temperature = 50f;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(reactor),
                "50 °C is at the threshold and must be refused");

            rbmk.Temperature = 0f;
            Assert.That(dock.FindLiveReactor(grid), Is.Null);

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            comp.Heat = 49.9f;
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "49.9 °C is below the threshold and storable");

            comp.Heat = 50f;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(drive),
                "50 °C is at the threshold and must be refused");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An RBMK alert is a store blocker on its own: it means the core is in a warning state even if
    /// the temperature watcher has already brought the number down or nobody has ticked it yet.
    /// </summary>
    [Test]
    public async Task RbmkAlertAloneBlocksStorage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var rbmk = entMan.GetComponent<RbmkReactorComponent>(reactor);

            rbmk.IsRunning = false;
            rbmk.Temperature = 0f;
            rbmk.AlertActive = true;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(reactor),
                "a core in an alert state must block the store");

            rbmk.AlertActive = false;
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a quiet, cold core should be storable");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every stormdrive warning state is a blocker, even on an otherwise idle and cold drive.
    /// </summary>
    [Test]
    public async Task StormdriveWarningStatesBlockStorage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            // Idle and cold, so only the warning flag is under test.
            comp.State = StormdriveState.Idle;
            comp.Heat = 0f;
            comp.ReactorEndTimes = false;

            foreach (var warning in new[] { StormdriveWarning.Overheat, StormdriveWarning.Meltdown })
            {
                comp.WarningState = warning;
                Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(drive),
                    $"the {warning} warning state must block the store");
            }

            comp.WarningState = StormdriveWarning.None;
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a quiet, cold drive should be storable");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Maintenance is an allowed state, but the heat gate still applies: a drive cooling down with
    /// its lid open is not storable until it is below the threshold.
    /// </summary>
    [Test]
    public async Task MaintenanceStateIsStorableOnlyWhileCold()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            comp.State = StormdriveState.Maintenance;
            comp.Heat = 0f;
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a cold drive in maintenance should be storable");

            comp.Heat = 60f;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(drive),
                "a hot drive in maintenance must still be refused");

            comp.Heat = 20f;
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a cool drive in maintenance should be storable");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// With several reactors on one grid the scan has to return the same offender every time; a
    /// nondeterministic pick would make the console message and the admin log disagree.
    /// </summary>
    [Test]
    public async Task MultipleReactorsReturnAStableOffender()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var first = entMan.SpawnEntity("RbmkNuclearReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var second = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "two cold reactors are storable");

            // Only the second one is live: the scan must name it, not the first.
            entMan.GetComponent<StormdriveComponent>(second).State = StormdriveState.Running;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(second),
                "the scan should find the one live reactor on the grid");

            // Both live: the answer must not bounce between them.
            entMan.GetComponent<RbmkReactorComponent>(first).IsRunning = true;
            var firstScan = dock.FindLiveReactor(grid);
            var secondScan = dock.FindLiveReactor(grid);
            Assert.That(firstScan, Is.Not.Null, "a grid with two live reactors must be blocked");
            Assert.That(secondScan, Is.EqualTo(firstScan),
                "two scans of the same grid returned different offenders");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A core that is already cold and stopped is ordinary cargo: the scram must leave it alone (the
    /// setpoint is player configuration) and must not count it as a live reactor it safed.
    /// </summary>
    [Test]
    public async Task SafeReactorsLeavesAlreadyColdStoppedCoresAlone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var rbmk = entMan.GetComponent<RbmkReactorComponent>(reactor);
            rbmk.IsRunning = false;
            rbmk.Temperature = 20f;
            rbmk.DesiredK = 1.5f;

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);
            comp.State = StormdriveState.Idle;
            comp.Heat = 0f;
            comp.LastPowerProduced = 123f;

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "sanity: both cores are already storable");

            Assert.That(dock.SafeReactors(grid), Is.EqualTo(0),
                "a cold, stopped core is not a live reactor and must not be counted as safed");
            Assert.That(rbmk.DesiredK, Is.EqualTo(1.5f).Within(0.001f),
                "the scram wiped a storable core's setpoint");
            Assert.That(rbmk.Temperature, Is.EqualTo(20f).Within(0.001f),
                "the scram changed an already-cold core's temperature");
            Assert.That(rbmk.IsRunning, Is.False, "a cold core came back running");
            Assert.That(comp.State, Is.EqualTo(StormdriveState.Idle), "the scram changed an idle drive's state");
            Assert.That(comp.Heat, Is.EqualTo(0f).Within(0.001f), "the scram heated an already-cold drive");
            Assert.That(comp.LastPowerProduced, Is.EqualTo(123f).Within(0.001f),
                "the scram deactivated an already-idle drive");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A flatpacked reactor is an item, not a live core: the machine does not exist until it is
    /// unpacked, so it must not block a ship from being stored (a crew hauling a spare flatpack is
    /// normal cargo). Once unpacked, the normal gate applies again.
    /// </summary>
    [Test]
    public async Task ReactorFlatpacksAreNotLiveCores()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            var rbmkPack = entMan.SpawnEntity("RbmkReactorFlatpack",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var drivePack = entMan.SpawnEntity("StormdriveReactorFlatpack",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            Assert.That(entMan.HasComponent<RbmkReactorComponent>(rbmkPack), Is.False,
                "sanity: a flatpack must not carry the reactor component");
            Assert.That(entMan.HasComponent<StormdriveComponent>(drivePack), Is.False,
                "sanity: a flatpack must not carry the stormdrive component");

            Assert.That(dock.FindLiveReactor(grid), Is.Null,
                "flatpacks are cargo; they must not count as live cores");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A malformed legacy core that throws while being safed must not abort the whole load: the
    /// failing core is logged and the other reactors on the grid are still safed. This mirrors the
    /// per-entity isolation in <c>RestoreMapInitRegistration</c>.
    /// </summary>
    [Test]
    public async Task OneReactorFailingToSafeDoesNotAbortTheOthers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var rbmk = EntityUid.Invalid;
        var drive = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;

            rbmk = entMan.SpawnEntity("RbmkNuclearReactorRunning",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var driveComp = entMan.GetComponent<StormdriveComponent>(drive);

            // The drive is live but idle, so Deactivate has no running-state announcement to send.
            driveComp.State = StormdriveState.Idle;
            driveComp.Heat = 300f;

            Assert.That(dock.FindLiveReactor(grid), Is.Not.Null, "sanity: the grid has live cores");

            // RbmkReactorSystem.ShutDown silences the core's ambient hum. Turn the hum on so that
            // call actually dirties the component. It has to happen on an earlier tick: Dirty skips
            // components already dirtied this tick, and the injected throw is on the dirty event.
            server.System<SharedAmbientSoundSystem>().SetAmbience(rbmk, true);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var grid = testMap.Grid.Owner;
            var rbmkComp = entMan.GetComponent<RbmkReactorComponent>(rbmk);
            var driveComp = entMan.GetComponent<StormdriveComponent>(drive);

            void ThrowOnRbmkDirty(Entity<MetaDataComponent> ent)
            {
                if (ent.Owner == rbmk)
                    throw new InvalidOperationException("injected reactor safing failure");
            }

            // The injected failure is logged at Error by design; do not let the test harness turn
            // that expected log into a test failure.
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var oldFailureLevel = cfg.GetCVar(RTCVars.FailureLogLevel);
            cfg.SetCVar(RTCVars.FailureLogLevel, LogLevel.Fatal);

            entMan.EntityDirtied += ThrowOnRbmkDirty;
            try
            {
                Assert.That(dock.SafeReactors(grid), Is.EqualTo(1),
                    "the healthy drive must still be safed when the RBMK throws");
            }
            finally
            {
                entMan.EntityDirtied -= ThrowOnRbmkDirty;
                cfg.SetCVar(RTCVars.FailureLogLevel, oldFailureLevel);
            }

            Assert.That(driveComp.Heat, Is.LessThan(50f), "the healthy drive was not safed");
            Assert.That(rbmkComp.IsRunning, Is.False,
                "the injected failure happens after ShutDown stopped the core");
        });

        await pair.CleanReturnAsync();
    }
}
