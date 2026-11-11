using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using NUnit.Framework;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     Palmtree: the reactors have to work as a "reactors only" slice - spawn the machine and its
///     fuel/rods straight from the prototype, tick it, and it runs. No cargo crate, no flatpack, no
///     research, no reactor console and no bluespace dock involved.
/// </summary>
/// <remarks>
///     This is the base/vanilla guarantee the reactor branch is supposed to keep: the machine is a
///     normal atmos machine whose only custom dependency is its own component and system. The test
///     deliberately never spawns a console, a circuitboard, a flatpack or a crate. If the simulation
///     only ran with the dock's save hook or a console link, this fails.
/// </remarks>
[TestFixture]
public sealed class ReactorBaseSliceTest
{
    [Test]
    public async Task DirectlySpawnedReactorsTickWithoutAcquisition()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid rbmk = default;
        EntityUid drive = default;

        await server.WaitAssertion(() =>
        {
            var containers = server.System<ContainerSystem>();
            var grid = testMap.Grid.Owner;
            var coords = new EntityCoordinates(grid, new Vector2(0.5f, 0.5f));

            // RBMK: the plain prototype, five uranium rods inserted by hand (the same container path
            // a player uses), and the precharge stand-in for a coolant/moderator loop. Nothing here
            // comes from cargo, research or a flatpack.
            rbmk = entMan.SpawnEntity("RbmkNuclearReactor", coords);
            var rbmkComp = entMan.GetComponent<RbmkReactorComponent>(rbmk);
            rbmkComp.Precharged = true;
            rbmkComp.TickInterval = 0.02f;

            Assert.That(containers.TryGetContainer(rbmk, RbmkReactorComponent.FuelContainer, out var fuel),
                Is.True, "the RBMK should declare its fuel container without a flatpack");

            for (var i = 0; i < 5; i++)
            {
                var rod = entMan.SpawnEntity("RbmkFuelRodUranium", coords);
                Assert.That(containers.Insert(rod, fuel!), Is.True, $"fuel rod {i} did not fit");
            }

            Assert.That(rbmkComp.FuelRods.Count, Is.EqualTo(5), "the reactor did not adopt its fuel rods");
            Assert.That(rbmkComp.IsRunning, Is.True, "the first inserted rod should start the core");

            // Stormdrive: the plain prototype, five standard control rods in the bay and a charged
            // fuel line. Ignition normally comes from the particle accelerator; setting the running
            // state directly keeps this test about the machine's own simulation.
            drive = entMan.SpawnEntity("StormdriveReactor", coords);
            var driveComp = entMan.GetComponent<StormdriveComponent>(drive);
            driveComp.ProcessInterval = 0.02f;

            Assert.That(containers.TryGetContainer(drive, StormdriveComponent.RodContainer, out var bay),
                Is.True, "the drive should declare its rod bay without a flatpack");

            for (var i = 0; i < 5; i++)
            {
                var rod = entMan.SpawnEntity("ControlRod", coords);
                Assert.That(containers.Insert(rod, bay!), Is.True, $"control rod {i} did not fit");
            }

            Assert.That(driveComp.ControlRods.Count, Is.EqualTo(5), "the drive did not adopt its control rods");
            Assert.That(driveComp.ControlRodIntegrity, Is.GreaterThan(0f), "the drive has no rod integrity");

            var nodes = entMan.GetComponent<NodeContainerComponent>(drive);
            var line = (PipeNode) nodes.Nodes[driveComp.FuelInputName];
            line.Air.Clear();
            line.Air.AdjustMoles(Gas.Plasma, 100f);
            line.Air.Temperature = Atmospherics.T20C;

            driveComp.Heat = 200f;
            driveComp.ReactionRate = 5f;
            driveComp.State = StormdriveState.Running;
        });

        // Both machines process on their own (the intervals are compressed so the test does not
        // need to wait the real two seconds per tick).
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var rbmkComp = entMan.GetComponent<RbmkReactorComponent>(rbmk);
            Assert.That(entMan.EntityExists(rbmk), Is.True, "the RBMK was deleted while ticking");
            Assert.That(rbmkComp.IsRunning, Is.True, "the RBMK stopped running without anyone touching it");
            Assert.That(rbmkComp.K, Is.GreaterThan(0f), "the RBMK simulation never produced reactivity");
            Assert.That(rbmkComp.Pressure, Is.GreaterThan(0f),
                "the RBMK's coolant outlet never got gas pressure (the tick never ran)");
            Assert.That(rbmkComp.LastCoolantTemperature, Is.LessThan(-100f),
                "the RBMK never consumed its precharged coolant (the tick never ran)");
            Assert.That(rbmkComp.Temperature, Is.Not.EqualTo(0f),
                "the RBMK's core temperature never moved (the tick never ran)");
            Assert.That(rbmkComp.Slagged, Is.False, "the RBMK melted down in half a second of simulation");

            var driveComp = entMan.GetComponent<StormdriveComponent>(drive);
            Assert.That(entMan.EntityExists(drive), Is.True, "the stormdrive was deleted while ticking");
            Assert.That(driveComp.State, Is.EqualTo(StormdriveState.Running),
                "the stormdrive stopped running without anyone touching it");
            Assert.That(driveComp.LastPowerProduced, Is.GreaterThan(0f),
                "the stormdrive simulation never produced power");
            Assert.That(driveComp.ControlRodIntegrity, Is.GreaterThan(0f),
                "the stormdrive lost all rod integrity in half a second of simulation");
        });

        await pair.CleanReturnAsync();
    }
}
