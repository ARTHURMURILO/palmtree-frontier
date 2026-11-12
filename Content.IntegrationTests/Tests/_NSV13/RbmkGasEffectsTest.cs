// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     The RBMK's new /tg/ moderator gases have to be more than names in a table: a gate full of
///     anti-noblium must actually produce more power and radiation than the same gate full of plasma.
/// </summary>
[TestFixture]
public sealed class RbmkGasEffectsTest
{
    [Test]
    public async Task AntiNobliumIsAReactorFuelAndARadiationSource()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid plasmaReactor = default;
        EntityUid antiReactor = default;

        await server.WaitAssertion(() =>
        {
            plasmaReactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", testMap.GridCoords);
            antiReactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", testMap.GridCoords);
        });

        // Let the atmos nodes finish wiring before anything is put in them.
        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            Configure(entMan, plasmaReactor, Gas.Plasma);
            Configure(entMan, antiReactor, Gas.AntiNoblium);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var plasma = entMan.GetComponent<RbmkReactorComponent>(plasmaReactor);
            var anti = entMan.GetComponent<RbmkReactorComponent>(antiReactor);

            Assert.That(plasma.LastPowerProduced, Is.GreaterThan(0f),
                "the plasma control reactor should produce power");
            Assert.That(anti.LastPowerProduced, Is.GreaterThan(plasma.LastPowerProduced * 2f),
                "anti-noblium has eight times plasma's fuel value; it should clearly out-produce it");
            Assert.That(anti.RawRadiation, Is.GreaterThan(plasma.RawRadiation),
                "anti-noblium should make the core a much stronger radiation source");
        });

        await pair.CleanReturnAsync();
    }

    private static void Configure(IEntityManager entMan, EntityUid uid, Gas gas)
    {
        var reactor = entMan.GetComponent<RbmkReactorComponent>(uid);

        // The running prototype is pre-charged for mapping. The test feeds the gate by hand so the
        // comparison is exactly one gas against the other.
        reactor.Precharged = false;
        reactor.IsRunning = true;
        reactor.DesiredK = 1f;
        reactor.Temperature = 200f;
        reactor.VesselIntegrity = 400f;
        reactor.NextTick = TimeSpan.Zero;

        var nodes = entMan.GetComponent<NodeContainerComponent>(uid);

        var coolantIn = (PipeNode) nodes.Nodes[reactor.CoolantInputName];
        coolantIn.Air.Clear();
        coolantIn.Air.AdjustMoles(Gas.Oxygen, 20f);
        coolantIn.Air.Temperature = Atmospherics.T20C;

        var coolantOut = (PipeNode) nodes.Nodes[reactor.CoolantOutputName];
        coolantOut.Air.Clear();

        var moderator = (PipeNode) nodes.Nodes[reactor.ModeratorInputName];
        moderator.Air.Clear();
        moderator.Air.AdjustMoles(gas, 20f);
        moderator.Air.Temperature = Atmospherics.T20C;
    }
    [Test]
    public async Task HealiumSlowsRodDepletion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid plasmaReactor = default;
        EntityUid healiumReactor = default;

        await server.WaitAssertion(() =>
        {
            plasmaReactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", testMap.GridCoords);
            healiumReactor = entMan.SpawnEntity("RbmkNuclearReactorRunning", testMap.GridCoords);
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            Configure(entMan, plasmaReactor, Gas.Plasma);
            Configure(entMan, healiumReactor, Gas.Healium);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var containers = entMan.System<SharedContainerSystem>();
            var plasmaRods = entMan.GetComponent<RbmkReactorComponent>(plasmaReactor);
            var healiumRods = entMan.GetComponent<RbmkReactorComponent>(healiumReactor);

            Assert.That(containers.TryGetContainer(plasmaReactor, RbmkReactorComponent.FuelContainer, out var plasmaBay),
                Is.True);
            Assert.That(containers.TryGetContainer(healiumReactor, RbmkReactorComponent.FuelContainer, out var healiumBay),
                Is.True);
            Assert.That(plasmaBay!.ContainedEntities, Is.Not.Empty);
            Assert.That(healiumBay!.ContainedEntities, Is.Not.Empty);

            var plasmaRod = entMan.GetComponent<RbmkFuelRodComponent>(plasmaBay.ContainedEntities[0]);
            var healiumRod = entMan.GetComponent<RbmkFuelRodComponent>(healiumBay.ContainedEntities[0]);

            Assert.That(healiumRod.Depletion, Is.LessThan(plasmaRod.Depletion),
                "healium should slow fuel-rod depletion, not just cancel a corrosive feed");
        });

        await pair.CleanReturnAsync();
    }

}
