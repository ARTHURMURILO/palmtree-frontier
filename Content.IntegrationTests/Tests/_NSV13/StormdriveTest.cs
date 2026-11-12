// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.ParticleAccelerator.Components;
using Content.Server.Power.Components;
using Content.Shared._NSV13.Reactor;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Content.Shared.Radiation.Components;
using Content.Shared.Singularity.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
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

    /// <summary>
    ///     The new console diagnosis must name the fuel-line branch the tick actually took: a rich
    ///     mix recovers, and an empty line starves. This is the data behind the stability bar.
    /// </summary>
    [Test]
    public async Task StabilityDiagnosisTracksTheFuelLine()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid uid = default;

        await server.WaitAssertion(() =>
        {
            uid = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
        });

        // The pipe node is wired during map init; writing gas before that is lost. This is the same
        // reason the older fuel-consumption test waits before filling the line.
        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var nodes = entMan.GetComponent<NodeContainerComponent>(uid);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];

            // Compress the clock so the test can see several process ticks without waiting 2 s each.
            comp.ProcessInterval = 0.05f;
            comp.NextProcess = TimeSpan.Zero;

            fuel.Air.Clear();
            fuel.Air.AdjustMoles(Gas.Plasma, 40f);
            fuel.Air.Temperature = Atmospherics.T20C;

            // Pin the drive below the stability cap so the rich branch has room to heal it.
            comp.Heat = 200f;
            comp.ReactionRate = 5f;
            comp.ReactorStability = 50f;
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);

            Assert.That(comp.LastStabilityCause, Is.EqualTo(StormdriveStabilityCause.Recovering),
                "a rich, under-pressure line should be reported as recovering");
            Assert.That(comp.LastStabilityDelta, Is.GreaterThan(0f),
                "recovering means the per-second rate is positive");

            var nodes = entMan.GetComponent<NodeContainerComponent>(uid);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];
            fuel.Air.Clear();
            comp.NextProcess = TimeSpan.Zero;
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);

            Assert.That(comp.LastStabilityCause, Is.EqualTo(StormdriveStabilityCause.Starved),
                "an empty line should be reported as starved");
            Assert.That(comp.LastStabilityDelta, Is.LessThan(0f),
                "starving means the per-second rate is negative");
            Assert.That(comp.ReactorStability, Is.LessThan(50f),
                "starving must cost stability");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The diagnosis must read the mixture <c>React()</c> charged, not the one the tick started
    ///     with: the slug leaves the line before the over-pressure check, so a line just above the
    ///     amber band still recovers instead of bleeding.
    /// </summary>
    [Test]
    public async Task StabilityDiagnosisUsesTheConsumedLine()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid uid = default;

        await server.WaitAssertion(() =>
        {
            uid = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);
            var nodes = entMan.GetComponent<NodeContainerComponent>(uid);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];

            // One process tick only: ten seconds between ticks keeps the next one far outside the
            // test window, so a second, safer tick cannot overwrite the cause under test.
            comp.ProcessInterval = 10f;
            comp.NextProcess = TimeSpan.Zero;

            // 82 moles at rate 5 with the 0.5 consumption factor: the tick starts above the
            // rate * 12 + 20 = 80 amber line, but React() removes 2.5 before it checks, leaving
            // 79.5 - under the line, so the rich mix recovers instead of bleeding.
            fuel.Air.Clear();
            fuel.Air.AdjustMoles(Gas.Plasma, 82f);
            fuel.Air.Temperature = Atmospherics.T20C;

            comp.Heat = 200f;
            comp.ReactionRate = 5f;
            comp.ReactorStability = 50f;
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(uid);

            Assert.That(comp.LastStabilityCause, Is.EqualTo(StormdriveStabilityCause.Recovering),
                "82 - 2.5 moles is under the 80 mole over-pressure line, so the rich mix recovers");
            Assert.That(comp.LastStabilityDelta, Is.GreaterThan(0f),
                "the rich mix should still be recovering");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The particle accelerator is the only ignition source the drive has: an accelerated
    ///     particle has to physically strike the core, add its heat and call <c>try_start</c>. This
    ///     pins down the two things that can silently break it, the fixture pair and the event pair,
    ///     by firing a real <c>ParticlesProjectile</c> at the machine instead of calling the handler.
    /// </summary>
    /// <remarks>
    ///     The projectile is spawned by hand because the three-emitter assembly is orthogonal to this
    ///     test: <c>ParticleAcceleratorSystem.FireEmitter</c> does exactly this spawn, sets the power
    ///     state and gives it a linear velocity. If this test fails, no PA in the game can start a
    ///     drive, which is what the portable emitter report looked like.
    /// </remarks>
    [Test]
    public async Task AcceleratedParticleHitsTheCoreAndIgnitesIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid drive = default;

        await server.WaitAssertion(() =>
        {
            drive = entMan.SpawnEntity("StormdriveReactor", testMap.GridCoords);

            // Machines anchor themselves on map init; only bodge it if this one did not.
            if (!entMan.GetComponent<TransformComponent>(drive).Anchored)
                entMan.System<SharedTransformSystem>().AnchorEntity(drive);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            // Keep the process tick (which zeroes an idle drive's heat) out of the test window.
            comp.NextProcess = TimeSpan.FromSeconds(30);

            SpawnParticle(entMan, testMap, ParticleAcceleratorPowerState.Level3);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            Assert.That(comp.Heat, Is.EqualTo(20f).Within(0.01f),
                "a level-3 particle must hit the core and deliver its heat; a value of 0 means it " +
                "passed through and the PA cannot start a drive at all");
            Assert.That(comp.State, Is.EqualTo(StormdriveState.Idle),
                "pre-heat alone must not start an empty line");
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);
            var nodes = entMan.GetComponent<NodeContainerComponent>(drive);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];

            fuel.Air.Clear();
            fuel.Air.AdjustMoles(Gas.Plasma, 100f);
            fuel.Air.Temperature = Atmospherics.T20C;

            SpawnParticle(entMan, testMap, ParticleAcceleratorPowerState.Level3);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            Assert.That(comp.State, Is.EqualTo(StormdriveState.Running),
                "a pre-heated drive with a charged line must ignite on the particle strike");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The handheld particle decelerator fires the same particle projectile through a plain
    ///     battery provider, so nothing ever assigns a PA power level. It must still heat the core:
    ///     this is the portable-emitter case that used to add zero heat and look inert.
    /// </summary>
    [Test]
    public async Task PortableEmitterBoltIgnitesTheCore()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid drive = default;

        await server.WaitAssertion(() =>
        {
            drive = entMan.SpawnEntity("StormdriveReactor", testMap.GridCoords);

            if (!entMan.GetComponent<TransformComponent>(drive).Anchored)
                entMan.System<SharedTransformSystem>().AnchorEntity(drive);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);
            comp.NextProcess = TimeSpan.FromSeconds(30);

            var nodes = entMan.GetComponent<NodeContainerComponent>(drive);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];
            fuel.Air.Clear();
            fuel.Air.AdjustMoles(Gas.Plasma, 100f);
            fuel.Air.Temperature = Atmospherics.T20C;

            // No power state assigned on purpose: this is exactly what the gun's ammo provider
            // produces.
            SpawnParticle(entMan, testMap, null, "NFAntiParticlesProjectile");
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);

            Assert.That(comp.State, Is.EqualTo(StormdriveState.Running),
                "a portable emitter bolt must carry enough energy to light a fuelled drive");
        });

        await pair.CleanReturnAsync();
    }

    private static void SpawnParticle(IEntityManager entMan, TestMapData testMap,
        ParticleAcceleratorPowerState? state, string prototype = "ParticlesProjectile")
    {
        var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0f, 3f));
        var particle = entMan.SpawnEntity(prototype, coords);

        // The collision gate keys off this fixture; a rename would silently disarm the drive.
        var fixtures = entMan.GetComponent<FixturesComponent>(particle);
        Assert.That(fixtures.Fixtures, Does.ContainKey("projectile"),
            $"{prototype} has no fixture named 'projectile': {string.Join(", ", fixtures.Fixtures.Keys)}");

        if (state is { } power)
            entMan.GetComponent<ParticleProjectileComponent>(particle).State = power;

        var physics = entMan.GetComponent<PhysicsComponent>(particle);
        entMan.System<SharedPhysicsSystem>().SetBodyStatus(particle, physics, BodyStatus.InAir);
        entMan.System<SharedPhysicsSystem>().SetLinearVelocity(particle, new Vector2(0f, -20f), body: physics);
    }

    /// <summary>
    ///     The Funky /tg/ gases have to reach the reaction maths and the console, not just exist as
    ///     Gas ids. Anti-noblium is the loudest of them: it should raise power, radiation and rod
    ///     wear while lowering the temperature thresholds.
    /// </summary>
    [Test]
    public async Task FunkyGasesDriveTheReactionMathAndConsoleRows()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid drive = default;
        EntityUid console = default;

        await server.WaitAssertion(() =>
        {
            drive = entMan.SpawnEntity("StormdriveReactorRunning", testMap.GridCoords);
            console = entMan.SpawnEntity("StormdriveControlConsole", testMap.GridCoords);
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);
            var consoleComp = entMan.GetComponent<StormdriveConsoleComponent>(console);
            var consoleSystem = entMan.System<StormdriveConsoleSystem>();

            Assert.That(consoleSystem.TryLinkReactor(console, consoleComp, drive), Is.True,
                "the test console should link to its drive");

            var nodes = entMan.GetComponent<NodeContainerComponent>(drive);
            var fuel = (PipeNode) nodes.Nodes[comp.FuelInputName];

            // One process tick only; 10 s keeps the next tick outside the test window.
            comp.ProcessInterval = 10f;
            comp.NextProcess = TimeSpan.Zero;
            comp.Heat = 200f;
            comp.ReactionRate = 5f;
            comp.ControlRodPercent = 33.6f;
            comp.ReactorStability = 100f;

            fuel.Air.Clear();
            fuel.Air.AdjustMoles(Gas.Plasma, 25f);
            fuel.Air.AdjustMoles(Gas.AntiNoblium, 25f);
            fuel.Air.Temperature = Atmospherics.T20C;

            consoleComp.NextSample = TimeSpan.Zero;
        });

        await pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<StormdriveComponent>(drive);
            var consoleComp = entMan.GetComponent<StormdriveConsoleComponent>(console);

            Assert.That(comp.InputPowerModifier, Is.GreaterThan(1.5f),
                "anti-noblium should raise the power multiplier");
            Assert.That(comp.RadiationModifier, Is.GreaterThan(5f),
                "anti-noblium should raise radiation");
            Assert.That(comp.TemperatureModifier, Is.LessThan(0.75f),
                "anti-noblium should lower the temperature thresholds");
            Assert.That(comp.ControlRodDegradationModifier, Is.GreaterThan(1.5f),
                "anti-noblium should wear the rods faster");

            var state = entMan.System<StormdriveConsoleSystem>().GetUiState(console, consoleComp);
            Assert.That(state.AntiNoblium, Is.GreaterThan(0f),
                "the console should report the anti-noblium in the fuel line");
            Assert.That(state.Plasma, Is.GreaterThan(0f),
                "the console should keep reporting the vanilla rows");
            Assert.That(state.GasHistory.Count, Is.EqualTo((int) StormdriveConsoleGas.Count),
                "the graph should have one history series for every gas row");
        });

        await pair.CleanReturnAsync();
    }

}
