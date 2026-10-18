using System.Numerics;
using Content.Server._PS.ShipCombat;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
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

    [Test]
    public async Task TargetDisplayNameFollowsIff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();
        var metaData = server.System<MetaDataSystem>();

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
            var consoleSystem = server.System<ShipTargetingConsoleSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);

            // No lock -> the window shows TARGET: NONE.
            var state = consoleSystem.BuildState(console, shipState.Comp);
            Assert.That(state.TargetName, Is.Null);

            // A visible target -> its name, truncated to 24 characters.
            var shuttles = server.System<SharedShuttleSystem>();
            if (!entMan.HasComponent<IFFComponent>(target))
                entMan.AddComponent<IFFComponent>(target);
            shuttles.AddIFFFlag(target, IFFFlags.IsPlayerShuttle);
            metaData.SetEntityName(target, "A Very Long Ship Name That Keeps Going");
            shipState.Comp.LockTarget = target;

            state = consoleSystem.BuildState(console, shipState.Comp);
            Assert.That(state.TargetName, Is.EqualTo("A Very Long Ship Name Th"),
                "A long target name should be truncated to 24 characters.");

            // A hidden-IFF target -> "?".
            shuttles.AddIFFFlag(target, IFFFlags.HideLabel);
            state = consoleSystem.BuildState(console, shipState.Comp);
            Assert.That(state.TargetName, Is.EqualTo("?"),
                "A target with a hidden IFF should show as unknown.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DamageRefreshesConsoleState()
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
        EntityUid wall = default;

        await server.WaitPost(() =>
        {
            (console, ship, _) = CreateShipPair(entMan, mapSystem, mapMan, xform, testMap.MapId, 100f);
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ship, new Vector2(2.5f, 0.5f)));
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var ui = server.System<SharedUserInterfaceSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);

            Assert.That(ui.TryGetUiState<TacticalConsoleBoundUserInterfaceState>(console, TacticalConsoleUiKey.Key,
                out var initialState), Is.True, "The console should have sent an initial state.");
            Assert.That(initialState!.Condition, Is.EqualTo(ShipCondition.Green));

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30f));

            Assert.That(shipState.Comp.Condition, Is.EqualTo(ShipCondition.Yellow));

            Assert.That(ui.TryGetUiState<TacticalConsoleBoundUserInterfaceState>(console, TacticalConsoleUiKey.Key,
                out var updated), Is.True);
            Assert.That(updated!.Condition, Is.EqualTo(ShipCondition.Yellow),
                "The console state should refresh when the condition changes.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LockingCannotBeSpammed()
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
        EntityUid other = default;

        await server.WaitPost(() =>
        {
            (console, ship, target) = CreateShipPair(entMan, mapSystem, mapMan, xform, testMap.MapId, 100f);

            var otherGrid = mapMan.CreateGridEntity(testMap.MapId);
            other = otherGrid.Owner;
            mapSystem.SetTile(other, otherGrid.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(other, new Vector2(-100f, 0f));
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var targeting = server.System<ShipTargetingSystem>();

            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);
            combat.SetCondition(shipState, ShipCondition.Yellow);
            shipState.Comp.WeaponMode = ShipWeaponMode.All;

            Assert.That(targeting.TryStartLock(console, target, 512f), Is.True);

            // While the lock is being acquired, spam-clicking cannot retarget.
            Assert.That(targeting.TryStartLock(console, other, 512f), Is.False,
                "A second lock request while acquiring must be ignored.");
            Assert.That(shipState.Comp.LockTarget, Is.EqualTo(target));
        });

        await pair.RunTicksSync(140);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var targeting = server.System<ShipTargetingSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);

            // Once the lock is complete, switching targets is allowed again.
            Assert.That(shipState.Comp.LockInProgress, Is.False);
            Assert.That(targeting.TryStartLock(console, other, 512f), Is.True,
                "Switching targets after the lock completed should work.");
            Assert.That(shipState.Comp.LockTarget, Is.EqualTo(other));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GreenConditionClearsLock()
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
            combat.SetCondition(shipState, ShipCondition.Yellow);
            shipState.Comp.WeaponMode = ShipWeaponMode.All;
            Assert.That(targeting.TryStartLock(console, target, 512f), Is.True);
        });

        await pair.RunTicksSync(140);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);
            Assert.That(combat.TryGetShipCombat(target, out var targetState), Is.True);
            Assert.That(shipState.Comp.LockTarget, Is.EqualTo(target));
            Assert.That(targetState.Comp.IncomingLocks, Does.Contain(ship));

            // Dropping to peacetime releases the lock on both sides.
            combat.SetCondition(shipState, ShipCondition.Green);
            Assert.That(shipState.Comp.LockTarget, Is.Null, "GREEN should clear the lock.");
            Assert.That(targetState.Comp.IncomingLocks, Does.Not.Contain(ship),
                "GREEN should stop the target from being locked.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AbsorbedHitsStillRaiseCondition()
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

            // 1 blunt is fully absorbed by the wall's metallic flat reduction: no damage is dealt,
            // but the ship is still under attack and should raise the alert.
            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            damageable.TryChangeDamage(wall, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 1f));

            Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Int(), Is.EqualTo(0),
                "The hit should have been fully absorbed.");
            Assert.That(shipState.Comp.Condition, Is.EqualTo(ShipCondition.Yellow),
                "Being attacked should raise the condition even when the hit is absorbed.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ContactsCarryThreatAndLockFlags()
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

            // The target is a ship: a shuttle console on it is enough for the contact scan.
            entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(target, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(70);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var consoleSystem = server.System<ShipTargetingConsoleSystem>();
            Assert.That(combat.TryGetShipCombat(ship, out var shipState), Is.True);

            // A target without ship combat state is not armed.
            var state = consoleSystem.BuildState(console, shipState.Comp);
            var contact = state.Contacts.Find(c => entMan.GetEntity(c.Grid) == target);
            Assert.That(contact, Is.Not.Null, "The ship in range should be a contact.");
            Assert.That(contact!.Armed, Is.False);
            Assert.That(contact.LockingUs, Is.False);

            // Off MASTER SAFE but still at GREEN: peacetime, so not a threat.
            var targetConsole = entMan.SpawnEntity("ComputerTacticalConsole",
                new EntityCoordinates(target, new Vector2(0.5f, 0.5f)));
            Assert.That(combat.TryGetShipCombat(target, out var targetState), Is.True);
            targetState.Comp.WeaponMode = ShipWeaponMode.All;

            state = consoleSystem.BuildState(console, shipState.Comp);
            contact = state.Contacts.Find(c => entMan.GetEntity(c.Grid) == target);
            Assert.That(contact!.Armed, Is.False, "A GREEN target is not a threat even with weapons armed.");

            // Armed and at YELLOW: renders red.
            combat.SetCondition(targetState, ShipCondition.Yellow);
            state = consoleSystem.BuildState(console, shipState.Comp);
            contact = state.Contacts.Find(c => entMan.GetEntity(c.Grid) == target);
            Assert.That(contact!.Armed, Is.True, "A target off MASTER SAFE at YELLOW is armed.");

            // Back on MASTER SAFE: no longer a threat.
            targetState.Comp.WeaponMode = ShipWeaponMode.MasterSafe;
            state = consoleSystem.BuildState(console, shipState.Comp);
            contact = state.Contacts.Find(c => entMan.GetEntity(c.Grid) == target);
            Assert.That(contact!.Armed, Is.False, "A target on MASTER SAFE is not a threat.");

            // A target locking this ship is flagged for the yellow cursor.
            shipState.Comp.IncomingLocks.Add(target);
            state = consoleSystem.BuildState(console, shipState.Comp);
            contact = state.Contacts.Find(c => entMan.GetEntity(c.Grid) == target);
            Assert.That(contact!.LockingUs, Is.True, "A ship locking us should be flagged.");
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
