// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Server.Power.Components;
using Content.Shared.Radiation.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._NSV13;

/// <summary>
///     The Stormdrive's miniature game: spawn a drive on a real grid and check the maths that the
///     whole machine hinges on.
/// </summary>
/// <remarks>
///     These are invariants, not a full simulation. The rod-to-temperature curve and the power
///     curve are the two things that decide whether the reactor is fun or broken, so they get pinned
///     here rather than discovered in game.
/// </remarks>
[TestFixture]
public sealed class StormdriveTest
{
    /// <summary>
    ///     The design-critical curve: <c>target_heat = -1 + 2^(0.1 * (100 - rods) * modifier)</c>.
    ///     Fully withdrawn rods aim at 1023 °C — past meltdown — and fully inserted rods aim at 0.
    ///     This is why "0%" is the most dangerous setting rather than the safest.
    /// </summary>
    [Test]
    public async Task RodPositionMapsOntoTargetHeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var cold = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            var coldComp = entMan.GetComponent<StormdriveComponent>(cold);
            coldComp.ControlRodPercent = 100f;

            var hot = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            var hotComp = entMan.GetComponent<StormdriveComponent>(hot);
            hotComp.ControlRodPercent = 0f;
        });

        // One tick is enough: NextProcess starts at zero, so the first update runs immediately.
        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var drives = entMan.EntityQueryEnumerator<StormdriveComponent>();
            float inserted = 0, withdrawn = 0;

            while (drives.MoveNext(out _, out var comp))
            {
                switch (comp.ControlRodPercent)
                {
                    case 100f:
                        inserted = comp.TargetHeat;
                        break;
                    case 0f:
                        withdrawn = comp.TargetHeat;
                        break;
                }
            }

            Assert.That(inserted, Is.EqualTo(0f).Within(0.01f), "fully inserted rods should aim at 0 C");
            Assert.That(withdrawn, Is.GreaterThan(1000f), "fully withdrawn rods should aim past meltdown");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     A running drive produces power, and the power scales with the cube of temperature.
    /// </summary>
    [Test]
    public async Task RunningDriveProducesPower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            var comp = entMan.GetComponent<StormdriveComponent>(uid);

            // Rods at 33.6% is the "rods_4" console preset: about 99 C target, a safe idle.
            comp.ControlRodPercent = 33.6f;
            comp.Heat = 200f;
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var drives = entMan.EntityQueryEnumerator<StormdriveComponent>();
            Assert.That(drives.MoveNext(out _, out var comp), Is.True, "drive should still exist");

            Assert.That(comp.Heat, Is.GreaterThan(0f), "core should stay warm");
            Assert.That(comp.LastPowerProduced, Is.GreaterThan(0f), "a running drive should produce power");
            Assert.That(comp.State, Is.EqualTo(StormdriveState.Running), "drive should still be running");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Arming the meltdown must not detonate immediately: the whole point is the eighteen second
    ///     window where control rods have failed but the crew still has time to run.
    /// </summary>
    [Test]
    public async Task MeltdownIsArmedBeforeItFires()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var system = entMan.System<StormdriveSystem>();

            Assert.That(comp.WarningState, Is.EqualTo(StormdriveWarning.None), "should start with no warning");

            system.StartMeltdown(uid, comp);

            Assert.That(comp.ReactorEndTimes, Is.True, "meltdown should be counting down");
            Assert.That(comp.WarningState, Is.EqualTo(StormdriveWarning.Meltdown), "warning should escalate");
            Assert.That(comp.State, Is.Not.EqualTo(StormdriveState.Meltdown), "must not detonate on arming");
            Assert.That(comp.MeltdownAt, Is.GreaterThan(TimeSpan.Zero), "countdown should be scheduled");
            Assert.That(comp.ControlRodIntegrity, Is.EqualTo(0f), "the rods fail first");

            // Arming twice must not restart or stack the countdown.
            var first = comp.MeltdownAt;
            system.StartMeltdown(uid, comp);
            Assert.That(comp.MeltdownAt, Is.EqualTo(first), "arming twice should be a no-op");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The drive must survive its own blast and stay on the grid as a wreck.
    /// </summary>
    /// <remarks>
    ///     The other meltdown assertions all run in the tick the meltdown was triggered, and the
    ///     explosion is queued for a later tick - so the machine looked fine right up until the next
    ///     tick deleted it. BaseMachine's Destructible thresholds remove the entity at 200 damage and
    ///     a meltdown deals thousands, which meant the "ruined core" the console kept reporting was
    ///     actually a hole in the deck.
    /// </remarks>
    [Test]
    public async Task MeltdownLeavesTheWreckBehind()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid uid = default;

        await server.WaitAssertion(() =>
        {
            uid = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            entMan.System<StormdriveSystem>().Meltdown(uid, comp);
        });

        // Let the queued explosion actually resolve.
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(uid), Is.True,
                "the melted-down drive must still exist as a wreck, not be deleted by its own blast");
            Assert.That(entMan.GetComponent<StormdriveComponent>(uid).State,
                Is.EqualTo(StormdriveState.Meltdown), "the surviving wreck should be in the meltdown state");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The actual detonation: a small blast, an EMP, a hot gas cloud and a permanently radiating
    ///     wreck - and deliberately no corium.
    /// </summary>
    /// <remarks>
    ///     An earlier version of this test asserted that a stormdrive meltdown scattered RBMK slag
    ///     across the ship. It does not, and should not: stormdrive.dm's <c>meltdown()</c> only
    ///     detonates the map's existing waste-spawner landmarks, and stacking the RBMK's slag storm
    ///     on top of the explosion made the two machines read as the same failure. The RBMK keeps
    ///     the slag; this test now guards the reverse, so the failure modes cannot quietly merge
    ///     back together.
    /// </remarks>
    [Test]
    public async Task MeltdownLeavesRadiatedWreckAndNoCorium()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var system = entMan.System<StormdriveSystem>();

            system.Meltdown(uid, comp);

            Assert.That(comp.State, Is.EqualTo(StormdriveState.Meltdown), "drive should be dead");
            Assert.That(comp.ReactionRate, Is.EqualTo(0f), "a dead drive consumes nothing");
            Assert.That(comp.LastPowerProduced, Is.EqualTo(0f), "a dead drive produces nothing");

            Assert.That(entMan.TryGetComponent<RadiationSourceComponent>(uid, out var source), Is.True);
            Assert.That(source!.Enabled, Is.True, "ruined core should radiate");
            Assert.That(source.Intensity, Is.EqualTo(comp.MeltdownRadiation).Within(0.01f),
                "ruined core radiation");

            // The drive must stop feeding the net: the wreck has no business exporting its last watt.
            Assert.That(entMan.TryGetComponent<PowerSupplierComponent>(uid, out var supplier), Is.True);
            Assert.That(supplier!.MaxSupply, Is.EqualTo(0f), "a ruined drive supplies nothing");

            var slag = 0;
            var query = entMan.EntityQueryEnumerator<RbmkSlagComponent>();
            while (query.MoveNext(out _, out _))
                slag++;

            Assert.That(slag, Is.Zero, "the stormdrive must not scatter corium - that is the RBMK's failure mode");
        });

        await pair.CleanReturnAsync();
    }
}
