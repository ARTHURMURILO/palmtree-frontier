using System.Numerics;
using Content.Server._NSV13.Reactor.Components;
using Content.Server._PS.BluespaceDock;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

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
}
