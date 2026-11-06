// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._NSV13.Reactor;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.DeviceLinking;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     The RBMK console: the multitool link that binds it to a reactor, and the setpoint it drives.
/// </summary>
/// <remarks>
///     The mechanics tests live in <see cref="RbmkSmokeTest"/>; these cover the layer the crew
///     actually touches, which is where the "you cannot run it without a console" gap was.
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
            typeof(RbmkWeldDoAfterEvent),
        };

        foreach (var type in events)
        {
            Assert.That(type.Assembly, Is.SameAs(shared), $"{type.Name} must live in Content.Shared");
            Assert.That(type.IsDefined(typeof(NetSerializableAttribute), inherit: false), Is.True,
                $"{type.Name} must be [NetSerializable]");
        }
    }

    [Test]
    public async Task RbmkConsoleBindsOnlyThroughAMultitoolLink()
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
            var links = entMan.System<DeviceLinkSystem>();

            // Standing next to a reactor must not bind anything any more: the link is the contract.
            system.ResolveReactor(console, consoleComp);
            Assert.That(consoleComp.Reactor, Is.Null, "proximity alone must not bind a console");

            // The multitool's "link defaults" is what creates the binding.
            links.LinkDefaults(null, console, reactor);
            system.ResolveReactor(console, consoleComp);

            Assert.That(consoleComp.Reactor, Is.EqualTo(reactor), "the link should bind the console");
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
            var reactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", shipB.GridCoords);
            var console = entMan.SpawnEntity("RbmkControlConsole", shipA.GridCoords);

            var consoleComp = entMan.GetComponent<RbmkConsoleComponent>(console);
            var system = entMan.System<RbmkConsoleSystem>();

            // Even a hand-made link must not cross grids: a console aboard one ship must never reach
            // the reactor of another flying in formation.
            Assert.That(system.TryLinkReactor(console, consoleComp, reactor), Is.False,
                "a cross-ship link has to be refused");

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
}
