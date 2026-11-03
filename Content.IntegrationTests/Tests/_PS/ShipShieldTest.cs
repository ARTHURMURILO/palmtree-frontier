using System.Numerics;
using Content.Server._PS.ShipCombat;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: directional ship shields (absorption, collapse/re-arm, regeneration, redistribution,
/// hardness and explosion absorption).
/// </summary>
[TestFixture]
public sealed class ShipShieldTest
{
    [Test]
    public async Task ShieldsAbsorbHitsAndDropTheRightFacing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid wall = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            (grid, _, _, wall) = CreateShieldedShip(entMan, mapSystem, mapMan, testMap.MapId);

            // An attacker grid east of the ship.
            var attackerGrid = mapMan.CreateGridEntity(testMap.MapId);
            mapSystem.SetTile(attackerGrid.Owner, attackerGrid.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(attackerGrid.Owner, new Vector2(20f, 0f));
            attacker = entMan.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(attackerGrid.Owner, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var shield = entMan.GetComponent<ShipShieldComponent>(grid);
            Assert.That(shield.Facings[(int) ShieldFacing.Starboard], Is.EqualTo(300f).Within(1f),
                "A fresh ship should have full shields.");

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30f),
                origin: attacker);

            Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Int(), Is.EqualTo(0),
                "The shield should absorb the whole hit.");
            Assert.That(shield.Facings[(int) ShieldFacing.Starboard], Is.LessThan(300f),
                "The facing towards the attacker should lose charge.");
            Assert.That(shield.Facings[(int) ShieldFacing.Fore], Is.EqualTo(300f).Within(1f),
                "Other facings should be untouched.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollapsedFacingLetsHitsThrough()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid wall = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            (grid, _, _, wall) = CreateShieldedShip(entMan, mapSystem, mapMan, testMap.MapId);

            var attackerGrid = mapMan.CreateGridEntity(testMap.MapId);
            mapSystem.SetTile(attackerGrid.Owner, attackerGrid.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(attackerGrid.Owner, new Vector2(20f, 0f));
            attacker = entMan.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(attackerGrid.Owner, new Vector2(0.5f, 0f)));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var shield = entMan.GetComponent<ShipShieldComponent>(grid);

            // Take the starboard facing down: hits from that side reach the hull.
            shield.Facings[(int) ShieldFacing.Starboard] = 0f;
            shield.Up[(int) ShieldFacing.Starboard] = false;

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30f),
                origin: attacker);

            Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Int(), Is.GreaterThan(0),
                "A collapsed facing must not absorb damage.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShieldsRegenerateAndReArm()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            (grid, _, _, _) = CreateShieldedShip(entMan, mapSystem, mapMan, testMap.MapId);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var shield = entMan.GetComponent<ShipShieldComponent>(grid);
            Assert.That(shield.Initialized, Is.True, "The generator should initialise the facings.");

            // A damaged facing regenerates; a collapsed one re-arms once it passes the threshold.
            shield.Facings[(int) ShieldFacing.Fore] = 100f;
            shield.Facings[(int) ShieldFacing.Aft] = 50f;
            shield.Up[(int) ShieldFacing.Aft] = false;
        });

        // One six-second pulse.
        await pair.RunTicksSync(400);

        await server.WaitAssertion(() =>
        {
            var shield = entMan.GetComponent<ShipShieldComponent>(grid);
            Assert.That(shield.Facings[(int) ShieldFacing.Fore], Is.GreaterThan(100f),
                "Shields should regenerate every six seconds.");
            Assert.That(shield.Up[(int) ShieldFacing.Aft], Is.True,
                "A collapsed facing should re-arm above 20% of its maximum.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RedistributionDrainsIntoOneFacing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            (grid, _, _, _) = CreateShieldedShip(entMan, mapSystem, mapMan, testMap.MapId);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var shields = server.System<ShipShieldSystem>();
            var shield = entMan.GetComponent<ShipShieldComponent>(grid);

            var totalBefore = 0f;
            foreach (var value in shield.Facings)
                totalBefore += value;

            Assert.That(shields.TryRedistribute(grid, ShieldFacing.Fore), Is.True);
            Assert.That(shield.Facings[(int) ShieldFacing.Fore], Is.EqualTo(300f).Within(1f),
                "The focused facing fills up to its maximum.");

            var totalAfter = 0f;
            foreach (var value in shield.Facings)
                totalAfter += value;

            Assert.That(totalAfter, Is.EqualTo(totalBefore).Within(1f),
                "Redistribution must not create or throw away shield energy.");
            Assert.That(shield.Facings[(int) ShieldFacing.Port], Is.GreaterThan(0f),
                "The other facings keep the energy the focused facing did not need.");

            // The redistribution has a cooldown.
            Assert.That(shields.TryRedistribute(grid, ShieldFacing.All), Is.False,
                "Redistribution should be on cooldown.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShieldHardnessReducesAbsorbedDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;
        EntityUid wall = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            (grid, _, _, wall) = CreateShieldedShip(entMan, mapSystem, mapMan, testMap.MapId);

            var attackerGrid = mapMan.CreateGridEntity(testMap.MapId);
            mapSystem.SetTile(attackerGrid.Owner, attackerGrid.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(attackerGrid.Owner, new Vector2(20f, 0f));
            attacker = entMan.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(attackerGrid.Owner, new Vector2(0.5f, 0f)));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var power = server.System<ShipPowerSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);

            // Shields at 100: hardness 20%.
            power.SetPool(state, ShipPowerPool.Shields, 100);

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 100f),
                origin: attacker);

            var shield = entMan.GetComponent<ShipShieldComponent>(grid);
            Assert.That(shield.Facings[(int) ShieldFacing.Starboard], Is.EqualTo(220f).Within(1f),
                "Hardness should reduce the damage the facing takes by 20%.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplosionTileDamageIsAbsorbed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            (grid, _, _, _) = CreateShieldedShip(entMan, mapSystem, mapMan, testMap.MapId);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var shield = entMan.GetComponent<ShipShieldComponent>(grid);
            var before = shield.Facings[(int) ShieldFacing.Starboard];

            var ev = new ShieldTileDamageAttemptEvent(new Vector2(20f, 0f), 50f);
            server.EntMan.EventBus.RaiseLocalEvent(grid, ref ev);

            Assert.That(ev.Cancelled, Is.True, "The shields should absorb the explosion's tile damage.");
            Assert.That(shield.Facings[(int) ShieldFacing.Starboard], Is.LessThan(before),
                "Absorbing the explosion should drain the facing towards it.");
        });

        await pair.CleanReturnAsync();
    }

    private static (EntityUid Grid, EntityUid Sink, EntityUid Generator, EntityUid Wall) CreateShieldedShip(
        IEntityManager entMan,
        SharedMapSystem mapSystem,
        IMapManager mapMan,
        MapId mapId)
    {
        var grid = mapMan.CreateGridEntity(mapId);
        mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 0), new Tile(1));
        mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 0), new Tile(1));
        mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(2, 0), new Tile(1));

        var sink = ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid.Owner, new Vector2(0.5f, 0.5f));
        var generator = entMan.SpawnEntity("ShieldGenerator", new EntityCoordinates(grid.Owner, new Vector2(1.5f, 0.5f)));
        var wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid.Owner, new Vector2(2.5f, 0.5f)));

        return (grid.Owner, sink, generator, wall);
    }
}
