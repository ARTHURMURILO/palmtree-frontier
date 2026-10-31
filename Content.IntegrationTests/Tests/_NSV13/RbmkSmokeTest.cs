// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Shared.Atmos;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     Smoke tests for the NSV13 RBMK port. These do not simulate a full reactor run — they check
///     that the prototypes load with the fields the C# side expects, that the fuel rod roster has
///     the documented power values, and that the reactor can spawn with its atmos nodes intact.
/// </summary>
[TestFixture]
public sealed class RbmkSmokeTest
{
    [Test]
    public async Task TestReactorGasesExist()
    {
        await using var pair = await PoolManager.GetServerClient();

        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            // Every gas the RBMK maths touches must resolve by its numeric id.
            // BRANCH NOTE (rbmk branch): constricted plasma, hyper-noblium, nitryl, BZ, pluoxium and
            // nucleium belong to the reactor-gas layer on `engines-all`; this branch's maths only
            // touches the vanilla set.
            foreach (var gas in new[]
                     {
                         Gas.Plasma, /* Gas.ConstrictedPlasma, */ Gas.Tritium, Gas.Oxygen,
                         Gas.Nitrogen, Gas.CarbonDioxide, Gas.WaterVapor,
                         /* Gas.HyperNoblium, Gas.Nitryl, Gas.BZ, Gas.Pluoxium, Gas.Nucleium, */
                     })
            {
                Assert.DoesNotThrow(() => protoMan.Index<Content.Shared.Atmos.Prototypes.GasPrototype>(((int) gas).ToString()),
                    $"Gas {gas} has no GasPrototype");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestFuelRodRosterMatchesSpec()
    {
        await using var pair = await PoolManager.GetServerClient();

        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            // Spec §A.3: uranium 0.10, plutonium 0.20, depleted 0.05.
            // NOTE: the depleted rod declares 0.05 but, exactly like the original, its first
            // MapInit/process pass overwrites fuel_power with og_fuel_power (0.20). That quirk is
            // preserved deliberately - see spec §D.9 - so the expectation here is 0.20, not 0.05.
            var expected = new (string Proto, float Power, float Threshold, bool SelfDecay)[]
            {
                ("RbmkFuelRodUranium", 0.10f, 100f, false),
                ("RbmkFuelRodPlutonium", 0.20f, 300f, true),
                ("RbmkFuelRodDepleted", 0.20f, 100f, true),
            };

            foreach (var (proto, power, threshold, selfDecay) in expected)
            {
                var uid = entMan.SpawnEntity(proto, MapCoordinates.Nullspace);
                var rod = entMan.GetComponent<RbmkFuelRodComponent>(uid);

                Assert.That(rod.FuelPower, Is.EqualTo(power).Within(0.0001f), $"{proto} fuel power");
                Assert.That(rod.DepletionThreshold, Is.EqualTo(threshold).Within(0.0001f), $"{proto} threshold");
                Assert.That(rod.SelfDecay, Is.EqualTo(selfDecay), $"{proto} self-decay");

                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestReactorSpawnsAndStartsUp()
    {
        await using var pair = await PoolManager.GetServerClient();

        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("RbmkNuclearReactor", MapCoordinates.Nullspace);
            var reactor = entMan.GetComponent<RbmkReactorComponent>(uid);

            // Fresh reactor state per spec §B.
            Assert.That(reactor.VesselIntegrity, Is.EqualTo(400f), "initial vessel integrity");
            Assert.That(reactor.Temperature, Is.EqualTo(0f), "initial temperature");
            Assert.That(reactor.K, Is.EqualTo(0f), "initial reactivity");
            Assert.That(reactor.Slagged, Is.False, "should not start slagged");
            Assert.That(reactor.IsRunning, Is.False, "should not auto-start");

            // §A.3 / §B.2: the per-round absorption constant is randomised to exactly 0.5 or 0.6.
            Assert.That(reactor.GasAbsorptionConstant, Is.AnyOf(0.5f, 0.6f), "absorption constant");

            // Loaded rods have to live somewhere that survives the insertion; deleting them used to
            // empty the core on the next tick. This catches the prototype container and the C#
            // constant drifting apart.
            var containers = entMan.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(uid, RbmkReactorComponent.FuelContainer, out _), Is.True,
                "reactor should declare its fuel container");

            var system = entMan.System<RbmkReactorSystem>();
            system.StartUp(uid, reactor);

            Assert.That(reactor.IsRunning, Is.True, "start_up should begin processing");
            Assert.That(reactor.DesiredK, Is.EqualTo(1f), "start_up sets desired_k to 1");

            // The mapper convenience subtype has to keep ticking from spawn. This is also what
            // catches the C# field name / YAML `running` data-field tag drifting apart.
            var prestarted = entMan.SpawnEntity("RbmkNuclearReactorRunning", MapCoordinates.Nullspace);
            Assert.That(entMan.GetComponent<RbmkReactorComponent>(prestarted).IsRunning, Is.True,
                "RbmkNuclearReactorRunning should start ticking");
            entMan.DeleteEntity(prestarted);

            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }
}
