// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Server._PS.Ships;
using Content.Shared.Radiation.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     A miniature game, spun up on demand, in which an RBMK is actually allowed to fail.
/// </summary>
/// <remarks>
///     The smoke tests next door only check that prototypes load. This one spawns a real grid,
///     bolts a reactor to it, and pulls the trigger, so the whole ship-scoped failure path runs
///     inside the test harness instead of needing a live client. That path is where the
///     interesting regressions live: the grid lookup, the slag storm, the one-shot latch.
/// </remarks>
[TestFixture]
public sealed class RbmkMeltdownTest
{
    [Test]
    public async Task MeltdownSlagsTheReactorAndLeavesContamination()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("RbmkNuclearReactor", testMap.GridCoords);
            var reactor = entMan.GetComponent<RbmkReactorComponent>(uid);
            var system = entMan.System<RbmkReactorSystem>();
            var ship = entMan.System<ShipEventSystem>();

            // Bisect the chain before detonating anything: the reactor must resolve a grid, that
            // grid must have floor tiles for the slag to land on, and the slag prototype must
            // actually carry its marker component.
            Assert.That(entMan.GetComponent<TransformComponent>(uid).GridUid, Is.Not.Null,
                "reactor should have resolved a grid");

            var shipTiles = ship.GetShipTiles(uid);
            Assert.That(shipTiles.Count, Is.GreaterThan(0), "reactor's ship should have floor tiles");

            var probe = entMan.SpawnEntity("RbmkSlag", testMap.GridCoords);
            Assert.That(entMan.HasComponent<RbmkSlagComponent>(probe), Is.True,
                "RbmkSlag prototype should carry RbmkSlagComponent");
            entMan.DeleteEntity(probe);

            Assert.That(reactor.Slagged, Is.False, "should not start slagged");

            system.Meltdown(uid, reactor);

            Assert.That(reactor.Slagged, Is.True, "meltdown should slag the reactor");
            Assert.That(reactor.IsRunning, Is.False, "a slagged reactor must not keep running");

            // The core stays behind as a permanent contamination source rather than a one-off pulse.
            Assert.That(entMan.TryGetComponent<RadiationSourceComponent>(uid, out var source), Is.True);
            Assert.That(source!.Intensity, Is.EqualTo(reactor.MeltdownRadiation).Within(0.01f),
                "slagged core radiation");
            Assert.That(source.Enabled, Is.True, "slagged core radiation should be enabled");

            // The slag storm replaces NSV13's map-wide fallout weather: corium is thrown across
            // the ship's own tiles and must actually land there. Counted in the same tick as the
            // meltdown so the queued explosion cannot perturb the tally.
            var slag = 0;
            var query = entMan.EntityQueryEnumerator<RbmkSlagComponent>();
            while (query.MoveNext(out _, out _))
                slag++;

            Assert.That(slag, Is.GreaterThan(0), "meltdown should scatter slag across the ship");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MeltdownOnlyFiresOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();

        var slagCount = 0;

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("RbmkNuclearReactor", testMap.GridCoords);
            var reactor = entMan.GetComponent<RbmkReactorComponent>(uid);
            var system = entMan.System<RbmkReactorSystem>();

            system.Meltdown(uid, reactor);

            var first = 0;
            var query = entMan.EntityQueryEnumerator<RbmkSlagComponent>();
            while (query.MoveNext(out _, out _))
                first++;

            // Several alert paths can reach Meltdown(), and the reactor keeps ticking for a moment
            // afterwards. A second call must not detonate the ship a second time.
            system.Meltdown(uid, reactor);
            system.Meltdown(uid, reactor);

            var second = 0;
            query = entMan.EntityQueryEnumerator<RbmkSlagComponent>();
            while (query.MoveNext(out _, out _))
                second++;

            slagCount = second;
            Assert.That(second, Is.EqualTo(first), "repeat meltdown calls must not spawn more slag");
        });

        Assert.That(slagCount, Is.GreaterThan(0), "the first meltdown should still have spawned slag");

        await pair.CleanReturnAsync();
    }
}
