// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Shared._NSV13.Reactor;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     The consoles, the rod bay and the appearance plumbing for both ported reactors.
/// </summary>
/// <remarks>
///     The mechanics tests live in <see cref="StormdriveTest"/> and <see cref="RbmkSmokeTest"/>; these
///     cover the layer the crew actually touches, which is where the "you cannot run it without a
///     console" gap was.
/// </remarks>
[TestFixture]
public sealed class ReactorConsolesTest
{
    /// <summary>
    ///     A DoAfterEvent is part of the DoAfterComponent's networked state, so
    ///     <c>SharedDoAfterSystem</c> requires [NetSerializable] - and a [NetSerializable] type must be
    ///     in Content.Shared, or it is only registered with NetSerializer on one side and shifts ids
    ///     out from under the client. This is a reflection-only guard for that pair of rules.
    /// </summary>
    [Test]
    public void ReactorDoAfterEventsAreSharedAndNetSerializable()
    {
        var shared = typeof(RbmkConsoleBoundUserInterfaceState).Assembly;
        var events = new[]
        {
            typeof(RbmkFuelInsertDoAfterEvent),
            typeof(RbmkSealantDoAfterEvent),
            typeof(StormdriveRodInstallDoAfterEvent),
            typeof(StormdriveRodRemoveDoAfterEvent),
            typeof(StormdriveRepairDoAfterEvent),
            typeof(StormdriveRepairStep),
        };

        foreach (var type in events)
        {
            Assert.That(type.Assembly, Is.SameAs(shared), $"{type.Name} must live in Content.Shared");
            Assert.That(type.IsDefined(typeof(NetSerializableAttribute), inherit: false), Is.True,
                $"{type.Name} must be [NetSerializable]");
        }
    }

    [Test]
    public async Task RbmkConsoleBindsOnItsOwnGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var reactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", testMap.GridCoords);
            var console = entMan.SpawnEntity("RbmkControlConsole", testMap.GridCoords);

            var consoleComp = entMan.GetComponent<RbmkConsoleComponent>(console);
            var system = entMan.System<RbmkConsoleSystem>();
            system.ResolveReactor(console, consoleComp);

            Assert.That(consoleComp.Reactor, Is.EqualTo(reactor), "console should drive the reactor");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RbmkConsoleIgnoresOtherShips()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var shipA = await pair.CreateTestMap();
        var shipB = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            entMan.SpawnEntity("RbmkNuclearReactorRunning", shipB.GridCoords);
            var console = entMan.SpawnEntity("RbmkControlConsole", shipA.GridCoords);

            var consoleComp = entMan.GetComponent<RbmkConsoleComponent>(console);
            var system = entMan.System<RbmkConsoleSystem>();
            system.ResolveReactor(console, consoleComp);

            Assert.That(consoleComp.Reactor, Is.Null,
                "a console must never reach across to another vessel's reactor");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RbmkConsoleClampsSetpointToCriticality()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var reactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", testMap.GridCoords);
            var console = entMan.SpawnEntity("RbmkControlConsole", testMap.GridCoords);
            var reactorComp = entMan.GetComponent<RbmkReactorComponent>(reactor);
            var consoleComp = entMan.GetComponent<RbmkConsoleComponent>(console);
            consoleComp.Reactor = reactor;

            // §G.1: desired_k = clamp(input, 0, 3). Both ends have to hold.
            entMan.EventBus.RaiseLocalEvent(console, new RbmkConsoleSetTargetMessage(99f));
            Assert.That(reactorComp.DesiredK, Is.EqualTo(3f).Within(0.001f), "clamped high");

            entMan.EventBus.RaiseLocalEvent(console, new RbmkConsoleSetTargetMessage(-99f));
            Assert.That(reactorComp.DesiredK, Is.EqualTo(0f).Within(0.001f), "clamped low");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StormdriveConsoleDrivesTheRodSetpoint()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var reactor = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            var console = entMan.SpawnEntity("StormdriveControlConsole", testMap.GridCoords);
            var reactorComp = entMan.GetComponent<StormdriveComponent>(reactor);
            var consoleComp = entMan.GetComponent<StormdriveConsoleComponent>(console);

            var system = entMan.System<StormdriveConsoleSystem>();
            system.ResolveReactor(console, consoleComp);
            Assert.That(consoleComp.Reactor, Is.EqualTo(reactor), "console should drive the drive");

            entMan.EventBus.RaiseLocalEvent(console, new StormdriveConsoleSetRodsMessage(33.6f));
            Assert.That(reactorComp.ControlRodPercent, Is.EqualTo(33.6f).Within(0.01f),
                "slider value should land on the reactor");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StormdriveMaintenanceOnlyFromIdle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("StormdriveReactor", testMap.GridCoords);
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var system = entMan.System<StormdriveSystem>();

            // Idle -> maintenance is the legal move.
            Assert.That(system.SetMaintenance(uid, comp, true), Is.True, "idle drive may open its lid");
            Assert.That(comp.State, Is.EqualTo(StormdriveState.Maintenance));

            Assert.That(system.SetMaintenance(uid, comp, false), Is.True, "and may close it again");
            Assert.That(comp.State, Is.EqualTo(StormdriveState.Idle));

            // A running core must refuse.
            comp.State = StormdriveState.Running;
            Assert.That(system.SetMaintenance(uid, comp, true), Is.False,
                "maintenance protocols cannot be engaged on a live core");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RodBayAveragesMountedRods()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var reactor = entMan.SpawnEntity("StormdriveReactor", testMap.GridCoords);
            var comp = entMan.GetComponent<StormdriveComponent>(reactor);
            var system = entMan.System<StormdriveSystem>();
            var containers = entMan.System<SharedContainerSystem>();

            var normal = entMan.SpawnEntity("ControlRod", testMap.GridCoords);
            var plasma = entMan.SpawnEntity("ControlRodPlasma", testMap.GridCoords);

            // The bay has to be declared by the prototype: the server and the client both need to
            // know about it before the first rod is inserted.
            Assert.That(containers.TryGetContainer(reactor, StormdriveComponent.RodContainer, out _),
                Is.True, "drive should declare its rod bay");

            // Insertion alone must be enough: the drive adopts whatever lands in its bay (this is how
            // ContainerFill-loaded and mapper-placed rods work too).
            var container = containers.EnsureContainer<Container>(reactor, StormdriveComponent.RodContainer);
            containers.Insert(normal, container);
            containers.Insert(plasma, container);

            Assert.That(comp.ControlRods, Has.Count.EqualTo(2), "bay insertion should register both rods");

            // (1.0 + -0.5) / 2 = 0.25 effectiveness; (100 + 100) / 2 = 100 integrity.
            Assert.That(comp.ControlRodModifier, Is.EqualTo(0.25f).Within(0.001f), "mean effectiveness");
            Assert.That(comp.ControlRodIntegrity, Is.EqualTo(100f).Within(0.001f), "mean integrity");

            // No rods at all: modifier 1 (so the temperature curve behaves), integrity 0 (no cooling).
            comp.ControlRods.Clear();
            system.RecalculateRods(comp);
            Assert.That(comp.ControlRodModifier, Is.EqualTo(1f).Within(0.001f), "empty bay modifier");
            Assert.That(comp.ControlRodIntegrity, Is.EqualTo(0f).Within(0.001f), "empty bay integrity");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VisualStatesFollowCoreHeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("StormdriveReactor", testMap.GridCoords);
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var system = entMan.System<StormdriveSystem>();
            var appearance = entMan.System<AppearanceSystem>();

            comp.State = StormdriveState.Running;
            comp.Heat = comp.TemperatureNominal + 1f;
            system.UpdateVisuals(uid, comp);

            Assert.That(appearance.TryGetData<StormdriveVisualState>(uid, StormdriveVisuals.Core, out var state), Is.True);
            Assert.That(state, Is.EqualTo(StormdriveVisualState.Hot), "hot core should use reactor_hot");

            comp.Heat = comp.TemperatureCritical + 1f;
            system.UpdateVisuals(uid, comp);

            appearance.TryGetData<StormdriveVisualState>(uid, StormdriveVisuals.Core, out state);
            Assert.That(state, Is.EqualTo(StormdriveVisualState.Overheat), "critical core should use reactor_overheat");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The soak test: give the drive real fuel gas and let it actually react. Before this, nothing
    ///     had ever executed <c>React()</c> outside a live server, so the gas maths was unproven.
    /// </summary>
    [Test]
    public async Task DriveConsumesFuelAndProducesPower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        EntityUid uid = default;

        await server.WaitAssertion(() =>
        {
            uid = entMan.SpawnEntity("StormdriveReactor", testMap.GridCoords);
        });

        // Let the atmos device finish wiring its pipe nodes before we put anything in them: the
        // node container is initialised during map init, and writing gas before that is lost.
        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            comp.ProcessInterval = 0.05f;

            // The first tick already scheduled itself 2 s out; pull it forward so the compressed
            // interval actually applies during the test.
            comp.NextProcess = TimeSpan.Zero;

            var nodes = entMan.GetComponent<NodeContainerComponent>(uid);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];

            fuel.Air.AdjustMoles(Gas.ConstrictedPlasma, 200f);
            fuel.Air.AdjustMoles(Gas.Plasma, 200f);
            fuel.Air.Temperature = 600f;

            comp.State = StormdriveState.Running;
            comp.Heat = 300f;
            comp.ReactionRate = 5f;
            comp.ReactorStability = 100f;
        });

        await pair.RunTicksSync(1);

        float before = 0f;
        float after = 0f;
        float power = 0f;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var nodes = entMan.GetComponent<NodeContainerComponent>(uid);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];
            before = fuel.Air.GetMoles(Gas.ConstrictedPlasma);

            Assert.That(before, Is.GreaterThan(0f), "the fuel line should hold gas");
        });

        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var nodes = entMan.GetComponent<NodeContainerComponent>(uid);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];

            after = fuel.Air.GetMoles(Gas.ConstrictedPlasma);
            power = comp.LastPowerProduced;

            Assert.That(after, Is.LessThan(before), "a running drive must eat its fuel");
            Assert.That(power, Is.GreaterThan(0f), "a running drive must produce power");
        });

        await pair.CleanReturnAsync();
    }
}
