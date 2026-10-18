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
/// Palmtree: ship locks and the damage reactions (automatic condition and retaliation).
/// </summary>
[TestFixture]
public sealed class ShipTargetingTest
{
    [Test]
    public async Task LockCompletesAfterDelay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid ship = default;
        EntityUid target = default;

        await server.WaitPost(() =>
        {
            (console, ship, target) = CreateShipPair(entMan, mapSystem, mapMan, xform, testMap.MapId, 100f);
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var targeting = server.System<ShipTargetingSystem>();

            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);

            // Peacetime: GREEN and MASTER SAFE both refuse the lock.
            Assert.That(targeting.TryStartLock(console, target, 512f), Is.False,
                "A peacetime ship must not lock.");

            combat.SetCondition(shipState, ShipCondition.Yellow);
            Assert.That(targeting.TryStartLock(console, target, 512f), Is.False,
                "A ship on MASTER SAFE must not lock.");

            shipState.Comp.WeaponMode = ShipWeaponMode.All;
            Assert.That(targeting.TryStartLock(console, target, 512f), Is.True,
                "An armed ship should start a lock.");
            Assert.That(shipState.Comp.LockInProgress, Is.True);
            Assert.That(shipState.Comp.LockTarget, Is.EqualTo(target));
        });

        // The lock takes two seconds.
        await pair.RunTicksSync(140);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var targeting = server.System<ShipTargetingSystem>();

            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);
            Assert.That(shipState.Comp.LockInProgress, Is.False, "The lock should have completed.");
            Assert.That(shipState.Comp.LockTarget, Is.EqualTo(target));

            Assert.That(combat.TryGetShipCombat(target, out var targetState), Is.True,
                "The locked grid should have combat state with the incoming lock.");
            Assert.That(targetState.Comp.IncomingLocks, Does.Contain(ship));

            Assert.That(targeting.TryClearLock(console), Is.True);
            Assert.That(shipState.Comp.LockTarget, Is.Null, "Clearing the lock should drop the target.");
            Assert.That(targetState.Comp.IncomingLocks, Does.Not.Contain(ship),
                "Clearing the lock should stop the target from being locked.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LockOutOfRangeIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid ship = default;
        EntityUid target = default;

        await server.WaitPost(() =>
        {
            (console, ship, target) = CreateShipPair(entMan, mapSystem, mapMan, xform, testMap.MapId, 5000f);
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var targeting = server.System<ShipTargetingSystem>();

            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);
            combat.SetCondition(shipState, ShipCondition.Yellow);
            shipState.Comp.WeaponMode = ShipWeaponMode.All;

            Assert.That(targeting.TryStartLock(console, target, 512f), Is.False,
                "A target outside the console's range must not be lockable.");
            Assert.That(shipState.Comp.LockInProgress, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DamageRaisesConditionAutomatically()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid ship = default;
        EntityUid wall = default;

        await server.WaitPost(() =>
        {
            (_, ship, _) = CreateShipPair(entMan, mapSystem, mapMan, xform, testMap.MapId, 100f);
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ship, new Vector2(2.5f, 0.5f)));
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);
            Assert.That(shipState.Comp.Condition, Is.EqualTo(ShipCondition.Green));

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30f));

            Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Int(), Is.GreaterThan(0),
                "The wall should take damage.");
            Assert.That(combat.TryGetShipCombat(wall, out _), Is.True,
                "The damaged entity should be part of the ship grid.");

            Assert.That(shipState.Comp.Condition, Is.EqualTo(ShipCondition.Yellow),
                "Damage to the ship should raise GREEN to YELLOW when automatic condition is on.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeingAttackedByAShipTriggersRetaliation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid ship = default;
        EntityUid target = default;
        EntityUid wall = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            (_, ship, target) = CreateShipPair(entMan, mapSystem, mapMan, xform, testMap.MapId, 100f);
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ship, new Vector2(2.5f, 0.5f)));
            // A ship weapon: a machine anchored on the attacking grid (not a mob).
            attacker = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(target, new Vector2(0.5f, 0.5f)));
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);
            shipState.Comp.AutomaticRetaliation = true;

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30f),
                origin: attacker);

            Assert.That(shipState.Comp.Condition, Is.EqualTo(ShipCondition.Yellow),
                "Retaliation should escalate out of peacetime.");
            Assert.That(shipState.Comp.WeaponMode, Is.EqualTo(ShipWeaponMode.All),
                "Retaliation should pull the ship out of MASTER SAFE.");
            Assert.That(shipState.Comp.LockInProgress, Is.True,
                "Retaliation should start a lock on the attacker.");
            Assert.That(shipState.Comp.LockTarget, Is.EqualTo(target));
        });

        await pair.RunTicksSync(140);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(target, out var targetState), Is.True);
            Assert.That(targetState.Comp.IncomingLocks, Does.Contain(ship),
                "The attacker should end up being locked.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeingAttackedByAPersonDoesNotTriggerRetaliation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid ship = default;
        EntityUid wall = default;
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            (_, ship, _) = CreateShipPair(entMan, mapSystem, mapMan, xform, testMap.MapId, 100f);
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ship, new Vector2(2.5f, 0.5f)));
            mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);
            shipState.Comp.AutomaticRetaliation = true;

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30f),
                origin: mob);

            Assert.That(shipState.Comp.Condition, Is.EqualTo(ShipCondition.Yellow),
                "The damage still raises the condition.");
            Assert.That(shipState.Comp.LockInProgress, Is.False,
                "A person with a gun is not a ship weapon and must not be retaliated against.");
        });

        await pair.CleanReturnAsync();
    }

    private static (EntityUid Console, EntityUid ShipGrid, EntityUid TargetGrid) CreateShipPair(
        IEntityManager entMan,
        SharedMapSystem mapSystem,
        IMapManager mapMan,
        SharedTransformSystem xform,
        MapId mapId,
        float targetDistance)
    {
        var ship = mapMan.CreateGridEntity(mapId);
        mapSystem.SetTile(ship.Owner, ship.Comp, new Vector2i(0, 0), new Tile(1));
        mapSystem.SetTile(ship.Owner, ship.Comp, new Vector2i(1, 0), new Tile(1));
        mapSystem.SetTile(ship.Owner, ship.Comp, new Vector2i(2, 0), new Tile(1));
        var console = entMan.SpawnEntity("ComputerTacticalConsole",
            new EntityCoordinates(ship.Owner, new Vector2(0.5f, 0.5f)));

        var target = mapMan.CreateGridEntity(mapId);
        mapSystem.SetTile(target.Owner, target.Comp, new Vector2i(0, 0), new Tile(1));
        xform.SetWorldPosition(target.Owner, new Vector2(targetDistance, 0f));

        return (console, ship.Owner, target.Owner);
    }
}
