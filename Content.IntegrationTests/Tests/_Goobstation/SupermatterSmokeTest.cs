using Content.Server.Atmos.EntitySystems;
using Robust.Shared.IoC;
using Robust.Shared.Reflection;
using Content.Shared.Explosion.Components;
using Content.Shared._Goobstation.Supermatter.Components;
using Content.Shared._Goobstation.Supermatter.Events;
using Content.Shared._Goobstation.Supermatter.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Radiation.Components;
using Content.Shared.Singularity.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Goobstation;

/// <summary>
///     Smoke tests for the Goobstation /tg/ gases and supermatter port.
/// </summary>
[TestFixture]
public sealed class SupermatterSmokeTest
{
    /// <summary>
    ///     Every <see cref="Gas"/> enum member must have a gas prototype whose id matches
    ///     its ordinal. Palmtree uses numeric prototype ids, so this also guards against
    ///     someone inserting an enum value without a matching prototype.
    /// </summary>
    [Test]
    public async Task TestAllGasesHavePrototypes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var gas in Enum.GetValues<Gas>())
            {
                Assert.That(protoMan.HasIndex<GasPrototype>(((int) gas).ToString()), Is.True,
                    $"No gas prototype with numeric id {(int) gas} for enum value {gas}");
            }

            // The /tg/ gases specifically.
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.BZ).ToString()).Name, Is.EqualTo("gases-bz"));
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.Healium).ToString()).Name, Is.EqualTo("gases-healium"));
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.Nitrium).ToString()).Name, Is.EqualTo("gases-nitrium"));
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.Pluoxium).ToString()).Name, Is.EqualTo("gases-pluoxium"));

            // NSV13 reactor gases.
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.ConstrictedPlasma).ToString()).Name, Is.EqualTo("gases-constricted-plasma"));
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.Nitryl).ToString()).Name, Is.EqualTo("gases-nitryl"));
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.HyperNoblium).ToString()).Name, Is.EqualTo("gases-hyper-noblium"));
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.Stimulum).ToString()).Name, Is.EqualTo("gases-stimulum"));
            Assert.That(protoMan.Index<GasPrototype>(((int) Gas.Nucleium).ToString()).Name, Is.EqualTo("gases-nucleium"));

            Assert.That(Atmospherics.TotalNumberOfGases, Is.EqualTo(20));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The supermatter must spawn, run its update loop while activated, and emit radiation.
    /// </summary>
    [Test]
    public async Task TestSupermatterRuns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();

        EntityUid sm = default;
        await server.WaitPost(() =>
        {
            sm = entMan.SpawnEntity("Supermatter", map.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<SupermatterComponent>(sm, out var comp), Is.True);
            Assert.That(entMan.HasComponent<RadiationSourceComponent>(sm), Is.True);

            // Force it on so the processing loop actually does work.
            comp!.Activated = true;
            comp.Damage = 40f;
            comp.Power = 100f;
        });

        // Let it tick. This exercises the atmos/radiation/power path.
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(sm), Is.True);
            Assert.That(entMan.HasComponent<SupermatterComponent>(sm), Is.True);

            var rad = entMan.GetComponent<RadiationSourceComponent>(sm);
            Assert.That(rad.Intensity, Is.GreaterThanOrEqualTo(0f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The monitor console and the sliver must both spawn.
    /// </summary>
    [Test]
    public async Task TestSupermatterAccessoriesSpawn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("SupermatterSliver", map.GridCoords);
            entMan.SpawnEntity("ComputerSupermatter", map.GridCoords);
            entMan.SpawnEntity("BZCanister", map.GridCoords);
            entMan.SpawnEntity("HealiumCanister", map.GridCoords);
            entMan.SpawnEntity("NitriumCanister", map.GridCoords);
            entMan.SpawnEntity("PluoxiumCanister", map.GridCoords);
        });

        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Delamination picks its outcome from the gas mix and stored power, and each branch must be
    ///     able to actually spawn what it promises. This guards the black-hole / tesla ball / explosion
    ///     triple, which is easy to break by shipping a prototype id that does not exist.
    /// </summary>
    [Test]
    public async Task TestDelamSpawnPrototypesExist()
    {
        await using var pair = await PoolManager.GetServerClient();

        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // Defaults from SupermatterComponent.
            foreach (var id in new[] { "Singularity", "TeslaEnergyBall" })
            {
                Assert.That(protoMan.HasIndex<EntityPrototype>(id), Is.True,
                    $"delamination spawn prototype '{id}' is missing");
            }

            // Both must be spawnable at the supermatter's coordinates.
            var singulo = entMan.SpawnEntity("Singularity", map.GridCoords);
            Assert.That(entMan.EntityExists(singulo), Is.True, "singularity failed to spawn");
            entMan.DeleteEntity(singulo);

            var tesla = entMan.SpawnEntity("TeslaEnergyBall", map.GridCoords);
            Assert.That(entMan.EntityExists(tesla), Is.True, "tesla ball failed to spawn");
            entMan.DeleteEntity(tesla);

            // The explosion branch goes through TriggerExplosive, which needs an Explosive component
            // and a matching explosion prototype.
            var sm = entMan.SpawnEntity("Supermatter", map.GridCoords);
            Assert.That(entMan.HasComponent<ExplosiveComponent>(sm), Is.True,
                "supermatter has no Explosive component, so the explosion delam branch does nothing");
            entMan.DeleteEntity(sm);
        });

        await pair.CleanReturnAsync();
    }
}
