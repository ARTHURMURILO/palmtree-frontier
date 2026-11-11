using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._NSV13.Reactor.Components;
using Content.Server._PS.BluespaceDock;
using Content.Shared.Storage;
using NUnit.Framework;
using Robust.Server.Containers;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: a reactor that goes through the bluespace dock has to come back as the same machine.
/// </summary>
/// <remarks>
/// The dock's gate refuses a running or hot core (<c>BluespaceDockReactorTest</c>), and the
/// reactors' <c>ComponentStartup</c> handlers rebuild their rod lists because a loaded ship skips
/// MapInit and restores containers as data without insert events. These tests use the real save and
/// load path (<see cref="BluespaceDockSystem.TrySaveShipFile"/> /
/// <see cref="BluespaceDockSystem.TryLoadShipFile"/>) so the two halves cannot drift apart: the file
/// must carry the permanent facts (fuel rods, vessel integrity, slagged, control rods, rod depletion)
/// and <see cref="BluespaceDockSystem.ResetRuntimeCooldowns"/> must strip the transient ones.
/// </remarks>
[TestFixture]
public sealed class BluespaceDockReactorSaveTest
{
    /// <summary>
    /// A cold, fuelled RBMK: five rods and a damaged vessel survive the round trip, and the
    /// retrieved core stays stopped instead of melting down on its own.
    /// </summary>
    [Test]
    public async Task ColdFuelledRbmkSurvivesTheRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;
        const float integrity = 312.5f;
        var absorption = 0f;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var containers = server.System<ContainerSystem>();
            var grid = testMap.Grid.Owner;

            // The pre-started prototype is the realistic fuelled core: ContainerFill puts five rods
            // in the fuel container during MapInit. It also starts running and hot, so stop it the
            // way a crew would before asking the dock to store the ship.
            var reactor = entMan.SpawnEntity("RbmkNuclearReactorRunning",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<RbmkReactorComponent>(reactor);

            Assert.That(comp.FuelRods.Count, Is.EqualTo(5),
                "sanity: the pre-started reactor should have five registered fuel rods");

            comp.IsRunning = false;
            comp.Temperature = 20f;
            comp.AlertActive = false;
            comp.VesselIntegrity = integrity;
            comp.DesiredK = 0.7f;

            // The save hook clears the RBMK's runtime clocks (they are absolute round times). Put
            // them far in the future so the assertion after the save proves the hook ran.
            comp.NextTick = TimeSpan.FromHours(1);
            comp.NextFlicker = TimeSpan.FromHours(1);

            absorption = comp.GasAbsorptionConstant;

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "sanity: a cold, stopped reactor is storable");

            var path = BluespaceDockSystem.GetShipFilePath("rbmk-cold-round-trip");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, error);

            Assert.That(comp.NextTick, Is.EqualTo(TimeSpan.Zero),
                "storing a ship must clear the RBMK's absolute tick timer");
            Assert.That(comp.NextFlicker, Is.EqualTo(TimeSpan.Zero),
                "storing a ship must clear the RBMK's absolute flicker timer");

            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);

            // Sanity on the file side: the rods are entities inside a container, not loose on the
            // grid, so they are only in the file if container contents were serialized.
            Assert.That(containers.TryGetContainer(reactor, RbmkReactorComponent.FuelContainer, out var fuel),
                Is.True);
            Assert.That(fuel!.ContainedEntities.Count, Is.EqualTo(5));
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var containers = server.System<ContainerSystem>();

            var loadedReactor = FindSingle<RbmkReactorComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedReactor, Is.Not.Null, "The retrieved ship lost its RBMK.");

            var comp = entMan.GetComponent<RbmkReactorComponent>(loadedReactor!.Value);
            Assert.That(comp.FuelRods.Count, Is.EqualTo(5),
                "The retrieved reactor did not rebuild its fuel-rod list from the container.");
            Assert.That(comp.FuelRods.All(uid => entMan.HasComponent<RbmkFuelRodComponent>(uid)), Is.True,
                "The retrieved reactor's rod list contains something that is not a fuel rod.");

            Assert.That(containers.TryGetContainer(loadedReactor.Value, RbmkReactorComponent.FuelContainer,
                out var fuel), Is.True);
            Assert.That(fuel!.ContainedEntities.Count, Is.EqualTo(5),
                "The retrieved reactor lost rods from its fuel container.");

            Assert.That(comp.VesselIntegrity, Is.EqualTo(integrity).Within(0.01f),
                "The retrieved reactor lost its vessel damage (a store/retrieve is not a free repair).");
            Assert.That(comp.Slagged, Is.False, "A healthy reactor came back slagged.");
            Assert.That(comp.Temperature, Is.EqualTo(20f).Within(0.01f),
                "The retrieved reactor's temperature did not survive the round trip.");
            Assert.That(comp.IsRunning, Is.False, "A stored cold reactor came back running.");
            Assert.That(comp.AlertActive, Is.False, "A stored cold reactor came back alerting.");
            Assert.That(comp.DesiredK, Is.EqualTo(0.7f).Within(0.001f),
                "The reactor's setpoint was not preserved (normalisation must not zero DesiredK).");
            Assert.That(comp.GasAbsorptionConstant, Is.EqualTo(absorption).Within(0.001f),
                "The reactor's per-round reactivity constant was not preserved.");

            Assert.That(dock.FindLiveReactor(loadedGrid), Is.Null,
                "The retrieved cold reactor still counts as a live core.");
        });

        // Let the retrieved reactor run for a while: it is not running and must not start itself or
        // melt down just because it was loaded (a loaded core has no coolant loop behind it).
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var loadedReactor = FindSingle<RbmkReactorComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedReactor, Is.Not.Null, "The retrieved reactor vanished while ticking.");

            var comp = entMan.GetComponent<RbmkReactorComponent>(loadedReactor!.Value);
            Assert.That(comp.Slagged, Is.False, "The retrieved cold reactor melted down on its own.");
            Assert.That(comp.IsRunning, Is.False, "The retrieved cold reactor started itself.");
            Assert.That(comp.FuelRods.Count, Is.EqualTo(5), "The retrieved reactor lost rods while ticking.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A slagged RBMK is legitimate cargo: the gate lets it through, and the wreck stays a wreck
    /// (still slagged, still not running) instead of being repaired by the round trip.
    /// </summary>
    [Test]
    public async Task SlaggedRbmkStaysWreckedAcrossTheRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactorRunning",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<RbmkReactorComponent>(reactor);

            // The state a meltdown leaves behind (Meltdown() sets these three).
            comp.Slagged = true;
            comp.IsRunning = false;
            comp.Temperature = 900f;
            comp.VesselIntegrity = 0f;

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "a slagged wreck must not block the store");

            var path = BluespaceDockSystem.GetShipFilePath("rbmk-slagged-round-trip");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, error);
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved wreck failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var loadedReactor = FindSingle<RbmkReactorComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedReactor, Is.Not.Null, "The retrieved ship lost its slagged reactor.");

            var comp = entMan.GetComponent<RbmkReactorComponent>(loadedReactor!.Value);
            Assert.That(comp.Slagged, Is.True, "A slagged reactor came back healthy (the store repaired it).");
            Assert.That(comp.IsRunning, Is.False, "A slagged reactor came back running.");
            Assert.That(comp.VesselIntegrity, Is.EqualTo(0f).Within(0.01f),
                "The retrieved wreck lost its destroyed vessel integrity.");
            Assert.That(comp.Temperature, Is.EqualTo(900f).Within(0.01f),
                "The retrieved wreck's temperature was altered by the store.");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var loadedReactor = FindSingle<RbmkReactorComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedReactor, Is.Not.Null, "The retrieved slagged reactor vanished while ticking.");

            var comp = entMan.GetComponent<RbmkReactorComponent>(loadedReactor!.Value);
            Assert.That(comp.Slagged, Is.True, "The retrieved slagged reactor stopped being a wreck.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A stormdrive with five control rods: the bay is rebuilt from the container, the rod integrity
    /// survives, and the transient fuse/warning/dump state is stripped by the save hook.
    /// </summary>
    [Test]
    public async Task FuelledStormdriveSurvivesTheRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;
        const float rodPercent = 33.6f;
        const string reactorId = "round-trip-probe";

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var containers = server.System<ContainerSystem>();
            var grid = testMap.Grid.Owner;

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            Assert.That(containers.TryGetContainer(drive, StormdriveComponent.RodContainer, out var bay),
                Is.True, "sanity: the stormdrive should declare its rod bay");

            for (var i = 0; i < 5; i++)
            {
                var rod = entMan.SpawnEntity("ControlRod", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                Assert.That(containers.Insert(rod, bay!), Is.True, $"sanity: control rod {i} did not fit");
            }

            Assert.That(comp.ControlRods.Count, Is.EqualTo(5),
                "sanity: the rods should be registered in the simulation");

            // Permanent player configuration: must survive.
            comp.ReactorId = reactorId;
            comp.ControlRodPercent = rodPercent;

            // Transient state the gate cannot see: DumpingFuel is serialized, so without the
            // normalization a stored drive would come back venting its fuel line into the waste pipe.
            comp.DumpingFuel = true;

            Assert.That(dock.FindLiveReactor(grid), Is.Null,
                "sanity: an idle, cold, unfused drive is storable");

            var path = BluespaceDockSystem.GetShipFilePath("stormdrive-round-trip");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, error);
            Assert.That(comp.DumpingFuel, Is.False, "storing a ship must clear the drive's fuel-dump toggle");

            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var containers = server.System<ContainerSystem>();
            var loadedDrive = FindSingle<StormdriveComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedDrive, Is.Not.Null, "The retrieved ship lost its stormdrive.");

            var comp = entMan.GetComponent<StormdriveComponent>(loadedDrive!.Value);
            Assert.That(comp.ControlRods.Count, Is.EqualTo(5),
                "The retrieved drive did not rebuild its control-rod list from the bay.");
            Assert.That(comp.ControlRodIntegrity, Is.GreaterThan(0f),
                "The retrieved drive has no control-rod integrity (it can never cool again).");

            Assert.That(containers.TryGetContainer(loadedDrive.Value, StormdriveComponent.RodContainer,
                out var bay), Is.True);
            Assert.That(bay!.ContainedEntities.Count, Is.EqualTo(5),
                "The retrieved drive lost rods from its bay.");

            Assert.That(comp.State, Is.EqualTo(StormdriveState.Idle), "The retrieved drive left the idle state.");
            Assert.That(comp.ReactorEndTimes, Is.False, "The retrieved drive came back with an armed meltdown fuse.");
            Assert.That(comp.WarningState, Is.EqualTo(StormdriveWarning.None),
                "The retrieved drive came back with a warning flag set.");
            Assert.That(comp.MeltdownAt, Is.EqualTo(TimeSpan.Zero),
                "The retrieved drive came back with a meltdown deadline from the previous round.");
            Assert.That(comp.DumpingFuel, Is.False,
                "The retrieved drive came back dumping fuel.");

            Assert.That(comp.ReactorId, Is.EqualTo(reactorId), "The drive's console link tag was not preserved.");
            Assert.That(comp.ControlRodPercent, Is.EqualTo(rodPercent).Within(0.001f),
                "The drive's control-rod setting was not preserved.");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var loadedDrive = FindSingle<StormdriveComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedDrive, Is.Not.Null, "The retrieved drive vanished while ticking.");

            var comp = entMan.GetComponent<StormdriveComponent>(loadedDrive!.Value);
            Assert.That(comp.ControlRodIntegrity, Is.GreaterThan(0f),
                "The retrieved drive's rod integrity fell to zero on its own.");
            Assert.That(comp.WarningState, Is.EqualTo(StormdriveWarning.None),
                "The retrieved idle drive raised a warning on its own.");
            Assert.That(comp.ReactorEndTimes, Is.False, "The retrieved idle drive armed a meltdown on its own.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The gate is the first line of defence: a running RBMK and a hot stormdrive are refused by
    /// <see cref="BluespaceDockSystem.TrySaveShipFile"/> itself (not only by the console UI), and no
    /// file is written. The admin scram safes both, after which the same store succeeds.
    /// </summary>
    [Test]
    public async Task LiveReactorNeverReachesTheShipFile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var containers = server.System<ContainerSystem>();
            var grid = testMap.Grid.Owner;

            var rbmk = entMan.SpawnEntity("RbmkNuclearReactorRunning",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var rbmkComp = entMan.GetComponent<RbmkReactorComponent>(rbmk);

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var driveComp = entMan.GetComponent<StormdriveComponent>(drive);
            Assert.That(containers.TryGetContainer(drive, StormdriveComponent.RodContainer, out var bay), Is.True);
            for (var i = 0; i < 5; i++)
            {
                var rod = entMan.SpawnEntity("ControlRod", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                Assert.That(containers.Insert(rod, bay!), Is.True);
            }

            // A running core and a hot (but idle) drive: two different blockers in one store attempt.
            rbmkComp.IsRunning = true;
            rbmkComp.Temperature = 20f;
            driveComp.State = StormdriveState.Idle;
            driveComp.Heat = 300f;

            Assert.That(dock.FindLiveReactor(grid), Is.Not.Null, "sanity: a live reactor should block the store");

            var path = BluespaceDockSystem.GetShipFilePath("live-reactor-store-test");
            var fullPath = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{path}");

            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.False,
                "the save path itself must refuse a live core, not only the console UI");
            Assert.That(error, Is.EqualTo("bluespace-dock-error-live-reactor-aboard"),
                "the store must report the live-reactor blocker");
            Assert.That(res.UserData.Exists(fullPath), Is.False,
                "a refused store must not leave a ship file behind");

            // The admin scram is the explicit way out: it safes both cores, then the store passes.
            Assert.That(dock.SafeReactors(grid), Is.EqualTo(2), "the scram should report both reactors");
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "the safed cores should be storable");

            Assert.That(dock.TrySaveShipFile(grid, path, out error), Is.True,
                $"saving after the scram failed: {error}");
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the safed ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();

            var loadedRbmk = FindSingle<RbmkReactorComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedRbmk, Is.Not.Null, "The retrieved ship lost its RBMK.");
            var rbmkComp = entMan.GetComponent<RbmkReactorComponent>(loadedRbmk!.Value);
            Assert.That(rbmkComp.IsRunning, Is.False, "A safed RBMK came back running.");
            Assert.That(rbmkComp.Temperature, Is.LessThan(50f), "A safed RBMK came back hot.");
            Assert.That(rbmkComp.FuelRods.Count, Is.EqualTo(5), "The safed RBMK lost its fuel rods.");

            var loadedDrive = FindSingle<StormdriveComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedDrive, Is.Not.Null, "The retrieved ship lost its stormdrive.");
            var driveComp = entMan.GetComponent<StormdriveComponent>(loadedDrive!.Value);
            Assert.That(driveComp.Heat, Is.LessThan(50f), "A safed drive came back hot.");
            Assert.That(driveComp.ReactorEndTimes, Is.False, "A safed drive came back with an armed fuse.");
            Assert.That(driveComp.ControlRods.Count, Is.EqualTo(5), "The safed drive lost its control rods.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Rod depletion is the one piece of fuel state the crew can see on the console, and it is
    /// serialized: a half-burnt rod must stay half-burnt (a store/retrieve is not a free refuel).
    /// Rod age is stored as a relative number of seconds for the same reason.
    /// </summary>
    [Test]
    public async Task FuelRodDepletionSurvivesTheRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;
        EntityUid savedRod = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactorRunning",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<RbmkReactorComponent>(reactor);

            comp.IsRunning = false;
            comp.Temperature = 10f;

            Assert.That(comp.FuelRods.Count, Is.EqualTo(5), "sanity: five fuel rods");
            savedRod = comp.FuelRods[0];
            var rod = entMan.GetComponent<RbmkFuelRodComponent>(savedRod);
            rod.Depletion = 42f;
            rod.AgeSeconds = 123f;

            var path = BluespaceDockSystem.GetShipFilePath("rbmk-depletion-round-trip");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, error);
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var loadedReactor = FindSingle<RbmkReactorComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedReactor, Is.Not.Null, "The retrieved ship lost its RBMK.");

            var comp = entMan.GetComponent<RbmkReactorComponent>(loadedReactor!.Value);
            var rods = comp.FuelRods
                .Select(uid => entMan.GetComponent<RbmkFuelRodComponent>(uid))
                .ToList();

            Assert.That(rods.Count, Is.EqualTo(5), "The retrieved reactor lost its fuel rods.");

            var depleted = rods.Where(rod => Math.Abs(rod.Depletion - 42f) < 0.001f).ToList();
            Assert.That(depleted.Count, Is.EqualTo(1),
                "The retrieved reactor lost the depleted rod's burn-up (docking refuelled it).");
            Assert.That(depleted[0].AgeSeconds, Is.EqualTo(123f).Within(0.001f),
                "The retrieved rod's relative age was not preserved.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The 2026-11-09 bluespace dock crash class, reactor side: a legacy ship file (or a live grid
    /// that was loaded before the cleanup) can hold container references to entities that no longer
    /// exist. The deserializer turns them into <see cref="EntityUid.Invalid"/>, and every traversal
    /// that walks the grid must skip them instead of asking a dead uid for its metadata. The reactor
    /// scan is new code that walks the same set, so it gets its own coverage.
    /// </summary>
    [Test]
    public async Task InvalidContainerReferencesDoNotBreakReactorScansOrSaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var containers = server.System<ContainerSystem>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<RbmkReactorComponent>(reactor);

            // A real container that also gets a dangling entry, exactly what a legacy file left
            // behind after deserialization. New files are sanitized on load, so the guard has to be
            // exercised directly.
            var toolbox = entMan.SpawnEntity("ToolboxMechanicalFilled",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            Assert.That(containers.TryGetContainer(toolbox, StorageComponent.ContainerId, out var storage),
                Is.True, "sanity: the toolbox has storage");

            var contained = (Container) storage!;
            var list = (List<EntityUid>) typeof(Container)
                .GetField("_containerList", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(contained)!;
            list.Add(EntityUid.Invalid);
            Assert.That(contained.ContainedEntities, Does.Contain(EntityUid.Invalid),
                "sanity: the dangling container reference is in place");

            // Every grid walk the dock and the reactor gate perform must tolerate it.
            var entities = new HashSet<EntityUid>();
            Assert.DoesNotThrow(() => dock.GetEntitiesOnGrid(grid, entities),
                "walking a grid with a dangling container reference must not throw");
            Assert.That(entities, Does.Not.Contain(EntityUid.Invalid),
                "the grid walk must not report a dangling reference as an entity");

            Assert.DoesNotThrow(() => dock.HasDangerousShipEntities(grid),
                "the dangerous-engine scan must tolerate a dangling reference");
            Assert.DoesNotThrow(() => dock.FindLiveReactor(grid),
                "the reactor scan must tolerate a dangling reference");
            Assert.DoesNotThrow(() => dock.PurgeDangerousShipEntities(grid),
                "the dangerous-engine purge must tolerate a dangling reference");

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "the cold reactor should still be storable");

            // The engine's strict serializer refuses to write a component that names an invalid uid
            // (and logs the error before the dock falls back to skipping that entity). That is the
            // documented, non-crashing behaviour; silence the log failure for the duration of the
            // save so the test harness does not turn the engine's expected error into a test error.
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var oldFailureLevel = cfg.GetCVar(RTCVars.FailureLogLevel);
            cfg.SetCVar(RTCVars.FailureLogLevel, LogLevel.Fatal);
            try
            {
                // The save path must store the grid, not throw on the dangling reference.
                var path = BluespaceDockSystem.GetShipFilePath("dangling-ref-reactor-save");
                Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True,
                    $"saving a grid with a dangling container reference failed: {error}");

                var fullPath = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{path}");
                string text;
                using (var reader = res.UserData.OpenText(fullPath))
                {
                    text = reader.ReadToEnd();
                }

                Assert.That(text, Does.Not.Contain("- invalid"),
                    "the written ship file kept a dangling container reference");

                dock.DeleteShipFile(path);
            }
            finally
            {
                cfg.SetCVar(RTCVars.FailureLogLevel, oldFailureLevel);
            }

            // And the gate still works on the same grid after the save.
            Assert.That(dock.FindLiveReactor(grid), Is.Null, "the cold reactor should still be storable");
            comp.IsRunning = true;
            Assert.That(dock.FindLiveReactor(grid), Is.EqualTo(reactor),
                "the reactor gate stopped detecting a running core");

            // Leave the container as it was; teardown should not try to delete a dangling uid.
            list.Remove(EntityUid.Invalid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A store that fails must not change the machine. The save hook clears the reactors' transient
    /// state before it writes, so every failure path has to put it back: the stormdrive's
    /// <c>DumpingFuel</c> is a serialized player toggle, and the RBMK's tick/flicker timers are live
    /// simulation state.
    /// </summary>
    [Test]
    public async Task FailedSaveRestoresReactorTransientState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var driveComp = entMan.GetComponent<StormdriveComponent>(drive);
            driveComp.State = StormdriveState.Idle;
            driveComp.Heat = 0f;
            driveComp.DumpingFuel = true;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var rbmk = entMan.GetComponent<RbmkReactorComponent>(reactor);
            rbmk.IsRunning = false;
            rbmk.Temperature = 20f;
            rbmk.NextTick = TimeSpan.FromHours(1);
            rbmk.NextFlicker = TimeSpan.FromHours(1);

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "sanity: both cores are storable");

            // Make the target path a directory: no file can be written there, so the save fails
            // before anything is stored.
            var path = BluespaceDockSystem.GetShipFilePath("failed-save-restores-transients");
            var blocked = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{path}");
            res.UserData.CreateDir(blocked);

            // The engine logs the expected write failure at Error level, which the test harness
            // treats as a test failure unless the failure log level is raised for the duration.
            var oldFailureLevel = cfg.GetCVar(RTCVars.FailureLogLevel);
            cfg.SetCVar(RTCVars.FailureLogLevel, LogLevel.Fatal);
            try
            {
                Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.False,
                    "sanity: a save into a directory must fail");
                Assert.That(error, Is.EqualTo("bluespace-dock-error-save-failed"));
            }
            finally
            {
                cfg.SetCVar(RTCVars.FailureLogLevel, oldFailureLevel);
                res.UserData.Delete(blocked);
            }

            Assert.That(driveComp.DumpingFuel, Is.True,
                "a failed save must not clear the drive's fuel-dump toggle");
            Assert.That(rbmk.NextTick, Is.EqualTo(TimeSpan.FromHours(1)),
                "a failed save must not clear the RBMK's tick timer");
            Assert.That(rbmk.NextFlicker, Is.EqualTo(TimeSpan.FromHours(1)),
                "a failed save must not clear the RBMK's flicker timer");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The load half of the normalisation. The save hook clears the transient toggles before the
    /// file is written, so a save -> load round trip cannot prove the load call does anything:
    /// edit the file between the two, as an older build or a hand edit would have left it.
    /// </summary>
    [Test]
    public async Task LoadPathClearsHandEditedStormdriveTransients()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;
        const string reactorId = "load-path-probe";
        const float rodPercent = 33.6f;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);
            comp.State = StormdriveState.Idle;
            comp.Heat = 0f;
            comp.ReactorId = reactorId;
            comp.ControlRodPercent = rodPercent;

            var path = BluespaceDockSystem.GetShipFilePath("hand-edited-stormdrive-load");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, error);

            var fullPath = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{path}");
            string text;
            using (var reader = res.UserData.OpenText(fullPath))
            {
                text = reader.ReadToEnd();
            }

            // The serializer omits values equal to their defaults, so the saved idle drive carries
            // none of these keys. Add them the way an older or hand-edited file would.
            const string marker = "    - type: Stormdrive\n";
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "sanity: the saved file has a stormdrive block");
            text = text.Insert(index + marker.Length,
                "      dumpingFuel: true\n" +
                "      reactorEndTimes: true\n" +
                "      warningState: Meltdown\n");

            using (var writer = res.UserData.OpenWriteText(fullPath))
            {
                writer.Write(text);
            }

            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the edited file failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var loadedDrive = FindSingle<StormdriveComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedDrive, Is.Not.Null, "The retrieved ship lost its stormdrive.");

            var comp = entMan.GetComponent<StormdriveComponent>(loadedDrive!.Value);
            Assert.That(comp.DumpingFuel, Is.False,
                "loading must clear a serialized fuel-dump toggle (the load half of the normalisation)");
            Assert.That(comp.ReactorEndTimes, Is.False, "loading must clear a serialized meltdown fuse.");
            Assert.That(comp.WarningState, Is.EqualTo(StormdriveWarning.None),
                "loading must clear a serialized warning flag.");
            Assert.That(comp.ReactorId, Is.EqualTo(reactorId), "loading lost the drive's console link tag.");
            Assert.That(comp.ControlRodPercent, Is.EqualTo(rodPercent).Within(0.001f),
                "loading lost the drive's control-rod setting.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Files written before the storage gate existed (or hand-edited ones) can carry a running or
    /// hot core. New files are always written cold and stopped, but a loaded ship must satisfy that
    /// invariant no matter where its file came from: the load path safes the offending core and
    /// logs it.
    /// </summary>
    [Test]
    public async Task HandEditedLiveReactorLoadsColdAndStopped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            var reactor = entMan.SpawnEntity("RbmkNuclearReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var rbmk = entMan.GetComponent<RbmkReactorComponent>(reactor);
            rbmk.IsRunning = false;
            rbmk.Temperature = 20f;
            rbmk.DesiredK = 0.7f;

            var drive = entMan.SpawnEntity("StormdriveReactor",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var comp = entMan.GetComponent<StormdriveComponent>(drive);
            comp.State = StormdriveState.Idle;
            comp.Heat = 0f;
            // A component whose fields all equal its prototype is omitted from the file entirely, so
            // give the drive a non-default field to make sure its block is the one we edit below.
            comp.ReactorId = "legacy-drive";

            Assert.That(dock.FindLiveReactor(grid), Is.Null, "sanity: a cold, stopped grid is storable");

            var path = BluespaceDockSystem.GetShipFilePath("hand-edited-live-reactor-load");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, error);

            var fullPath = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{path}");
            string text;
            using (var reader = res.UserData.OpenText(fullPath))
            {
                text = reader.ReadToEnd();
            }

            // Make the file look like one written by a build without the gate: a running RBMK at
            // 800 °C and a running stormdrive at 800 heat.
            const string rbmkMarker = "    - type: RbmkReactor\n";
            var rbmkIndex = text.IndexOf(rbmkMarker, StringComparison.Ordinal);
            Assert.That(rbmkIndex, Is.GreaterThanOrEqualTo(0), "sanity: the saved file has an RBMK block");
            text = text.Insert(rbmkIndex + rbmkMarker.Length, "      running: true\n");
            text = text.Replace("      temperature: 20\n", "      temperature: 800\n");

            const string driveMarker = "    - type: Stormdrive\n";
            var driveIndex = text.IndexOf(driveMarker, StringComparison.Ordinal);
            Assert.That(driveIndex, Is.GreaterThanOrEqualTo(0), "sanity: the saved file has a stormdrive block");
            text = text.Insert(driveIndex + driveMarker.Length,
                "      state: Running\n" +
                "      heat: 800\n");

            using (var writer = res.UserData.OpenWriteText(fullPath))
            {
                writer.Write(text);
            }

            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the edited file failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();

            var loadedReactor = FindSingle<RbmkReactorComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedReactor, Is.Not.Null, "The retrieved ship lost its RBMK.");
            var rbmk = entMan.GetComponent<RbmkReactorComponent>(loadedReactor!.Value);
            Assert.That(rbmk.IsRunning, Is.False, "a legacy running core came back running");
            Assert.That(rbmk.Temperature, Is.LessThan(50f), "a legacy hot core came back hot");
            Assert.That(rbmk.DesiredK, Is.EqualTo(0f).Within(0.001f),
                "the load-time scram should zero the setpoint of a running core");

            var loadedDrive = FindSingle<StormdriveComponent>(dock, entMan, loadedGrid);
            Assert.That(loadedDrive, Is.Not.Null, "The retrieved ship lost its stormdrive.");
            var drive = entMan.GetComponent<StormdriveComponent>(loadedDrive!.Value);
            Assert.That(drive.State, Is.EqualTo(StormdriveState.Idle),
                "a legacy running drive came back outside Idle");
            Assert.That(drive.Heat, Is.LessThan(50f), "a legacy hot drive came back hot");

            Assert.That(dock.FindLiveReactor(loadedGrid), Is.Null,
                "the loaded ship still violates the cold-and-stopped invariant");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Finds the one entity on a loaded grid that has <typeparamref name="TComp"/>, or null.
    /// </summary>
    private static EntityUid? FindSingle<TComp>(BluespaceDockSystem dock, IEntityManager entMan, EntityUid grid)
        where TComp : IComponent
    {
        var entities = new HashSet<EntityUid>();
        dock.GetEntitiesOnGrid(grid, entities);

        EntityUid? found = null;
        foreach (var uid in entities)
        {
            if (!entMan.HasComponent<TComp>(uid))
                continue;

            if (found != null)
                Assert.Fail($"More than one {typeof(TComp).Name} on the loaded grid.");

            found = uid;
        }

        return found;
    }
}
