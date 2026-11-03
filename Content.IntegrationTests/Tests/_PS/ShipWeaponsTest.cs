using System.Collections.Generic;
using System.Numerics;
using Content.Server._PS.ShipCombat;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._PS.CCVar;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;
using Content.Server.Shuttles.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: console-controlled ship weapons (group linking, manual fire, gating) and the rule that
/// a ship's own weapons bypass its shields.
/// </summary>
[TestFixture]
public sealed class ShipWeaponsTest
{
    [Test]
    public async Task ManualFireSpawnsProjectiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId, server.System<SharedDeviceLinkSystem>());
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.Primary;
            state.Comp.LockTarget = targetGrid;

            var before = CountProjectiles(entMan);
            Assert.That(server.System<ShipWeaponSystem>().TryManualFire(console), Is.True,
                "Manual fire should fire the linked turret.");
            Assert.That(CountProjectiles(entMan), Is.GreaterThan(before),
                "Firing should have spawned a projectile.");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<BluespacePowerSinkComponent, PowerConsumerComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var consumer, out var xform))
            {
                if (xform.GridUid != grid)
                    continue;

                Assert.That(consumer.DrawRate, Is.GreaterThanOrEqualTo(2000f),
                    "A bolt shot should add a 2 kW transient draw to the relay.");
                return;
            }

            Assert.Fail("No transmitter on the grid.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MasterSafeAndGreenBlockFiring()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId, server.System<SharedDeviceLinkSystem>());
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);

            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.All;
            state.Comp.LockTarget = targetGrid;

            // Peacetime: no firing.
            combat.SetCondition(state, ShipCondition.Green);
            Assert.That(weapons.TryManualFire(console), Is.False, "GREEN must not fire.");
            Assert.That(CountProjectiles(entMan), Is.EqualTo(0));

            // Armed but on MASTER SAFE: no firing.
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.MasterSafe;
            Assert.That(weapons.TryManualFire(console), Is.False, "MASTER SAFE must not fire.");
            Assert.That(CountProjectiles(entMan), Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnpoweredWeaponsDoNotFire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId, server.System<SharedDeviceLinkSystem>());
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.All;
            state.Comp.LockTarget = targetGrid;

            // Cut the generator: the weapon loses power on the next power tick.
            FindSupplier(entMan, grid).Enabled = false;
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(entMan.GetComponent<BluespacePoweredComponent>(turret).Powered, Is.False,
                "The turret should be unpowered.");
            Assert.That(weapons.TryManualFire(console), Is.False, "An unpowered weapon must not fire.");
            Assert.That(CountProjectiles(entMan), Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TurretsDoNotFireOutsideTheirArc()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId, server.System<SharedDeviceLinkSystem>());

            // Put the target behind the turret (it faces east): outside the 45-degree arc.
            xform.SetWorldPosition(targetGrid, new Vector2(-40f, 0f));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.All;
            state.Comp.LockTarget = targetGrid;

            Assert.That(weapons.TryManualFire(console), Is.False,
                "A turret must not fire at a target outside its arc.");
            Assert.That(CountProjectiles(entMan), Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OwnWeaponsBypassOwnShields()
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
        EntityUid weapon = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            for (var x = 0; x < 4; x++)
                mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(x, 0), new Tile(1));

            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            entMan.SpawnEntity("ShieldGenerator", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            weapon = entMan.SpawnEntity("ShipStationaryWeapon", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)));

            // An attacker on another grid.
            var attackerGrid = mapMan.CreateGridEntity(testMap.MapId);
            mapSystem.SetTile(attackerGrid.Owner, attackerGrid.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(attackerGrid.Owner, new Vector2(40f, 0f));
            attacker = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(attackerGrid.Owner, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30f);

            // The ship's own weapon: the shield must not absorb it.
            damageable.TryChangeDamage(wall, damage, origin: weapon);
            Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Int(), Is.GreaterThan(0),
                "A ship's own weapons must bypass its shields.");

            // An outside attacker: absorbed by the shield.
            damageable.TryChangeDamage(wall, damage, origin: attacker);
            var shield = entMan.GetComponent<ShipShieldComponent>(grid);
            Assert.That(shield.Facings[(int) ShieldFacing.Starboard], Is.LessThan(300f),
                "An outside attacker should drain the facing towards it.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AimModesPickSubsystemsOnTheTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid targetGrid = default;
        EntityUid hull = default;
        EntityUid thruster = default;
        EntityUid generator = default;

        await server.WaitPost(() =>
        {
            var target = mapMan.CreateGridEntity(testMap.MapId);
            targetGrid = target.Owner;
            for (var x = 0; x < 4; x++)
                mapSystem.SetTile(targetGrid, target.Comp, new Vector2i(x, 0), new Tile(1));

            hull = entMan.SpawnEntity("WallSolid", new EntityCoordinates(targetGrid, new Vector2(0.5f, 0.5f)));

            thruster = entMan.SpawnEntity("WallSolid", new EntityCoordinates(targetGrid, new Vector2(1.5f, 0.5f)));
            entMan.AddComponent<ThrusterComponent>(thruster);

            generator = entMan.SpawnEntity("WallSolid", new EntityCoordinates(targetGrid, new Vector2(2.5f, 0.5f)));
            entMan.AddComponent<PowerSupplierComponent>(generator);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var weapons = server.System<ShipWeaponSystem>();

            Assert.That(weapons.GetAimTarget(targetGrid, ShipTargetMode.Random), Is.Null,
                "RANDOM aims at the grid centre, not an entity.");
            Assert.That(weapons.GetAimTarget(targetGrid, ShipTargetMode.Hull), Is.EqualTo(hull),
                "HULL should pick the plain structure.");
            Assert.That(weapons.GetAimTarget(targetGrid, ShipTargetMode.Thrusters), Is.EqualTo(thruster),
                "THRUSTERS should pick the thruster.");
            Assert.That(weapons.GetAimTarget(targetGrid, ShipTargetMode.Generators), Is.EqualTo(generator),
                "GENERATORS should pick the power source.");
            Assert.That(weapons.GetAimTarget(targetGrid, ShipTargetMode.Weapons), Is.Null,
                "The target has no weapons.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeamTurretFiresThroughOwnHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;
        EntityUid ownWall = default;
        EntityUid targetWall = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            for (var x = 0; x < 5; x++)
                mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(x, 0), new Tile(1));

            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            console = entMan.SpawnEntity("ComputerTacticalConsole", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            turret = entMan.SpawnEntity("ShipTurretBeam", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));

            // Face the turret east (its arc is centred on the angle it was placed at).
            xform.SetLocalRotationNoLerp(turret, Angle.FromDegrees(90));
            entMan.GetComponent<ShipWeaponComponent>(turret).BaseAngle = Angle.FromDegrees(90);

            // A wall of our own hull sits in the firing line: the beam has to pass through it.
            ownWall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)));

            var target = mapMan.CreateGridEntity(testMap.MapId);
            targetGrid = target.Owner;
            mapSystem.SetTile(targetGrid, target.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(targetGrid, new Vector2(40f, 0f));
            targetWall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(targetGrid, new Vector2(0.5f, 0.5f)));

            var deviceLink = server.System<SharedDeviceLinkSystem>();
            deviceLink.ToggleLink(null, console, turret, "ShipWeaponLink", "ShipWeaponPrimary");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.Primary;
            state.Comp.LockTarget = targetGrid;

            // The cvar restores the original shoot-through behaviour; without it the turret would
            // hold fire because its own hull is in the line.
            server.CfgMan.SetCVar(PSCVars.ShipCombatTurretsShootThroughHull, true);

            Assert.That(server.System<ShipWeaponSystem>().TryManualFire(console), Is.True,
                "The beam turret should fire.");

            Assert.That(entMan.GetComponent<DamageableComponent>(targetWall).TotalDamage.Int(), Is.GreaterThan(0),
                "The beam should hit the target through our own hull.");
            Assert.That(entMan.GetComponent<DamageableComponent>(ownWall).TotalDamage.Int(), Is.EqualTo(0),
                "The beam must not hit our own hull.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TurretsHoldFireWhenHullBlocksTheShot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            for (var x = 0; x < 5; x++)
                mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(x, 0), new Tile(1));

            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            console = entMan.SpawnEntity("ComputerTacticalConsole", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            turret = entMan.SpawnEntity("ShipTurret", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));

            xform.SetLocalRotationNoLerp(turret, Angle.FromDegrees(90));
            entMan.GetComponent<ShipWeaponComponent>(turret).BaseAngle = Angle.FromDegrees(90);

            // Our own hull blocks the firing line.
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)));

            var target = mapMan.CreateGridEntity(testMap.MapId);
            targetGrid = target.Owner;
            mapSystem.SetTile(targetGrid, target.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(targetGrid, new Vector2(40f, 0f));

            var deviceLink = server.System<SharedDeviceLinkSystem>();
            deviceLink.ToggleLink(null, console, turret, "ShipWeaponLink", "ShipWeaponPrimary");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.Primary;
            state.Comp.LockTarget = targetGrid;

            // Opt in to hold-fire (the cvar defaults to shooting through).
            server.CfgMan.SetCVar(PSCVars.ShipCombatTurretsShootThroughHull, false);

            Assert.That(weapons.TryManualFire(console), Is.False,
                "A turret whose own hull blocks the line must hold fire.");
            Assert.That(state.Comp.WeaponsBlocked, Is.True,
                "The console must be able to explain why the weapons are silent.");
            Assert.That(CountProjectiles(entMan), Is.EqualTo(0));

            // The cvar restores the original shoot-through behaviour.
            server.CfgMan.SetCVar(PSCVars.ShipCombatTurretsShootThroughHull, true);
            Assert.That(weapons.TryManualFire(console), Is.True,
                "The cvar should restore shooting through the ship's own hull.");
            Assert.That(CountProjectiles(entMan), Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeaponsDoNotFireWhileTheLockIsAcquiring()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId,
                server.System<SharedDeviceLinkSystem>());
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.Primary;

            // A lock that is still being acquired must not fire the weapons.
            state.Comp.LockTarget = targetGrid;
            state.Comp.LockInProgress = true;

            Assert.That(weapons.TryManualFire(console), Is.False,
                "Weapons must not fire before the lock is set.");
            Assert.That(CountProjectiles(entMan), Is.EqualTo(0));

            // Once the lock is set they fire.
            state.Comp.LockInProgress = false;
            Assert.That(weapons.TryManualFire(console), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrewAimModePicksLivingThings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid targetGrid = default;
        EntityUid corgi = default;

        await server.WaitPost(() =>
        {
            var target = mapMan.CreateGridEntity(testMap.MapId);
            targetGrid = target.Owner;
            // A 3x3 grid so the corgi cannot wander off the single tile it spawned on.
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                    mapSystem.SetTile(targetGrid, target.Comp, new Vector2i(x, y), new Tile(1));
            }

            entMan.SpawnEntity("WallSolid", new EntityCoordinates(targetGrid, new Vector2(2.5f, 2.5f)));
            corgi = entMan.SpawnEntity("MobCorgi", new EntityCoordinates(targetGrid, new Vector2(1.5f, 1.5f)));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var weapons = server.System<ShipWeaponSystem>();

            Assert.That(weapons.GetAimTarget(targetGrid, ShipTargetMode.Crew), Is.EqualTo(corgi),
                "CREW should aim at living things on the target.");

            // The dead are not living things.
            var proto = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 500f);
            server.System<DamageableSystem>().TryChangeDamage(corgi, damage);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(weapons.GetAimTarget(targetGrid, ShipTargetMode.Crew), Is.Null,
                "CREW should ignore corpses.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TurretsTrackTheLockedTargetWithoutAutoFire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId,
                server.System<SharedDeviceLinkSystem>());

            // Put the target south east of the turret, still inside its 45 degree arc.
            xform.SetWorldPosition(targetGrid, new Vector2(22.5f, -6.8f));
        });

        await pair.RunTicksSync(5);

        var startRotation = 0d;

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.Primary;
            state.Comp.LockTarget = targetGrid;

            startRotation = entMan.GetComponent<TransformComponent>(turret).LocalRotation.Degrees;
            Assert.That(state.Comp.AutomaticFire, Is.False, "Auto-fire stays off in this test.");
        });

        // The turret should turn towards the locked target even though it will not shoot.
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var rotation = entMan.GetComponent<TransformComponent>(turret).LocalRotation.Degrees;
            Assert.That(rotation, Is.LessThan(startRotation - 5d),
                $"The turret should track the locked target (was {startRotation}, now {rotation}).");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TorpedoesLaunchSteerAndShowOnTheConsole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            for (var x = 0; x < 3; x++)
                mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(x, 0), new Tile(1));

            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            console = entMan.SpawnEntity("ComputerTacticalConsole", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));

            // A launcher at the east edge, its muzzle facing open space.
            var launcher = entMan.SpawnEntity("ShipTorpedoLauncher", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
            xform.SetLocalRotationNoLerp(launcher, Angle.FromDegrees(90));

            var target = mapMan.CreateGridEntity(testMap.MapId);
            targetGrid = target.Owner;
            mapSystem.SetTile(targetGrid, target.Comp, new Vector2i(0, 0), new Tile(1));
            // North east of the launcher, inside the seeker arc: the torpedo has to steer after
            // launching east.
            xform.SetWorldPosition(targetGrid, new Vector2(60f, 40f));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var torpedoes = server.System<ShipTorpedoSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.All;

            // No firing at all without a set lock.
            state.Comp.LockTarget = targetGrid;
            state.Comp.LockInProgress = true;
            Assert.That(torpedoes.TryLaunch(console), Is.False, "Torpedoes need a set lock.");
            state.Comp.LockInProgress = false;

            Assert.That(torpedoes.TryLaunch(console), Is.True, "The launcher should fire.");

            // The launcher's cooldown holds back an immediate second volley.
            Assert.That(torpedoes.TryLaunch(console), Is.False, "Each launcher has its own cooldown.");

        });

        // Before the steering delay the torpedo must fly straight out of the tube, not turn at the
        // target immediately (which used to send it back through its own ship).
        await pair.RunTicksSync(30);

        EntityUid torpedo = default;

        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<Content.Shared._PS.ShipCombat.Components.ShipTorpedoComponent>();
            Assert.That(query.MoveNext(out torpedo, out var torpedoComp), Is.True, "A torpedo should exist.");
            Assert.That(torpedoComp.Target, Is.EqualTo(targetGrid));
            Assert.That(torpedoComp.Source, Is.EqualTo(grid));

            // The console shows a friendly torpedo blip.
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            var consoleState = server.System<ShipTargetingConsoleSystem>().BuildState(console, state.Comp);
            Assert.That(consoleState.Torpedoes, Has.Count.EqualTo(1), "The torpedo should be on the IFF.");
            Assert.That(consoleState.Torpedoes[0].Friendly, Is.True);

            var velocity = entMan.GetComponent<PhysicsComponent>(torpedo).LinearVelocity;
            Assert.That(velocity.Length(), Is.GreaterThan(0.5f), "The torpedo should be moving.");
            Assert.That(Math.Abs(velocity.ToWorldAngle().Degrees - 90d), Is.LessThan(10d),
                "The torpedo must fly straight out of the launcher before steering.");
            Assert.That(xform.GetWorldPosition(torpedo).X, Is.GreaterThan(3f),
                "The torpedo must spawn in front of the launcher, not on top of the ship.");
        });

        // After the 2 s delay it starts turning towards the target.
        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(torpedo), Is.True, "The torpedo should still be in flight.");
            var velocity = entMan.GetComponent<PhysicsComponent>(torpedo).LinearVelocity;

            // East is Angle.FromDegrees(90); steering north puts the heading well away from it.
            Assert.That(Math.Abs(velocity.ToWorldAngle().Degrees - 90d), Is.GreaterThan(10d),
                "The torpedo should steer towards the target after the delay.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TorpedoesLaunchForwardEvenWhenTheTargetIsBehind()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            for (var x = 0; x < 3; x++)
                mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(x, 0), new Tile(1));

            ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
            console = entMan.SpawnEntity("ComputerTacticalConsole", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));

            // Launcher facing north (180 degrees in this angle convention), target to the south.
            var launcher = entMan.SpawnEntity("ShipTorpedoLauncher", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
            xform.SetLocalRotationNoLerp(launcher, Angle.FromDegrees(180));

            var target = mapMan.CreateGridEntity(testMap.MapId);
            targetGrid = target.Owner;
            mapSystem.SetTile(targetGrid, target.Comp, new Vector2i(0, 0), new Tile(1));
            xform.SetWorldPosition(targetGrid, new Vector2(0f, -40f));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var torpedoes = server.System<ShipTorpedoSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.All;
            state.Comp.LockTarget = targetGrid;

            Assert.That(torpedoes.TryLaunch(console), Is.True, "The launcher should fire.");
        });

        await pair.RunTicksSync(30);

        EntityUid torpedo = default;

        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<Content.Shared._PS.ShipCombat.Components.ShipTorpedoComponent>();
            Assert.That(query.MoveNext(out torpedo, out _), Is.True,
                "The torpedo must not detonate on its own ship.");

            var velocity = entMan.GetComponent<PhysicsComponent>(torpedo).LinearVelocity;
            Assert.That(velocity.Y, Is.GreaterThan(0f),
                "A north-facing launcher must fire north even when the target is south.");
            Assert.That(xform.GetWorldPosition(torpedo).Y, Is.GreaterThan(2f),
                "The torpedo must spawn in front of the launcher, not on top of the ship.");
        });

        // The target is behind the seeker arc: the torpedo must give up rather than turn 180 into
        // its own ship.
        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(torpedo), Is.True, "The torpedo should still be in flight.");
            var torpedoComp = entMan.GetComponent<Content.Shared._PS.ShipCombat.Components.ShipTorpedoComponent>(torpedo);
            Assert.That(torpedoComp.SteeringAbandoned, Is.True, "The torpedo should give up on a target behind it.");

            var velocity = entMan.GetComponent<PhysicsComponent>(torpedo).LinearVelocity;
            Assert.That(velocity.Y, Is.GreaterThan(0f), "The torpedo should keep flying forwards after giving up.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FloorsDoNotBlockTurretFire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId,
                server.System<SharedDeviceLinkSystem>());
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.Primary;
            state.Comp.LockTarget = targetGrid;

            // The strictest setting: hold fire when the hull blocks the line. The shot still crosses
            // the ship's own floor tiles on the way out, which must not count as blocking.
            server.CfgMan.SetCVar(PSCVars.ShipCombatTurretsShootThroughHull, false);

            Assert.That(weapons.TryManualFire(console), Is.True,
                "The ship's own floor tiles must not block a turret.");
            Assert.That(state.Comp.WeaponsBlocked, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallOnTheTurretsOwnTileBlocksFire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid console = default;
        EntityUid grid = default;
        EntityUid turret = default;
        EntityUid targetGrid = default;

        await server.WaitPost(() =>
        {
            (console, grid, turret, targetGrid) = CreateArmedShip(entMan, mapSystem, mapMan, xform, testMap.MapId,
                server.System<SharedDeviceLinkSystem>());

            // The turret is mounted in the hull: a wall sits on its own tile, so the shot would hit
            // it immediately.
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            var weapons = server.System<ShipWeaponSystem>();
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            combat.SetCondition(state, ShipCondition.Yellow);
            state.Comp.WeaponMode = ShipWeaponMode.Primary;
            state.Comp.LockTarget = targetGrid;

            server.CfgMan.SetCVar(PSCVars.ShipCombatTurretsShootThroughHull, false);

            Assert.That(weapons.TryManualFire(console), Is.False,
                "A turret embedded in a hull wall must hold fire.");
            Assert.That(state.Comp.WeaponsBlocked, Is.True);

            // Shooting through the hull (the default) makes the same turret fire again.
            server.CfgMan.SetCVar(PSCVars.ShipCombatTurretsShootThroughHull, true);
            Assert.That(weapons.TryManualFire(console), Is.True,
                "The cvar must restore shooting through the hull.");
        });

        await pair.CleanReturnAsync();
    }

    private static (EntityUid Console, EntityUid Grid, EntityUid Turret, EntityUid TargetGrid) CreateArmedShip(
        IEntityManager entMan,
        SharedMapSystem mapSystem,
        IMapManager mapMan,
        SharedTransformSystem xform,
        MapId mapId,
        SharedDeviceLinkSystem deviceLink)
    {
        var gridEnt = mapMan.CreateGridEntity(mapId);
        var grid = gridEnt.Owner;
        for (var x = 0; x < 4; x++)
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(x, 0), new Tile(1));

        ShipCombatTestHelpers.SpawnPoweredTransmitter(entMan, mapSystem, grid, new Vector2(0.5f, 0.5f));
        var console = entMan.SpawnEntity("ComputerTacticalConsole", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
        var turret = entMan.SpawnEntity("ShipTurret", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));

        // Face the turret east (its arc is centred on the angle it was placed at).
        xform.SetLocalRotationNoLerp(turret, Angle.FromDegrees(90));
        entMan.GetComponent<ShipWeaponComponent>(turret).BaseAngle = Angle.FromDegrees(90);

        var target = mapMan.CreateGridEntity(mapId);
        var targetGrid = target.Owner;
        mapSystem.SetTile(targetGrid, target.Comp, new Vector2i(0, 0), new Tile(1));
        xform.SetWorldPosition(targetGrid, new Vector2(40f, 0f));

        // Link the console to the turret on the Primary port.
        deviceLink.ToggleLink(null, console, turret, "ShipWeaponLink", "ShipWeaponPrimary");

        return (console, grid, turret, targetGrid);
    }

    private static int CountProjectiles(IEntityManager entMan)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out _, out _))
        {
            count++;
        }

        return count;
    }

    private static PowerSupplierComponent FindSupplier(IEntityManager entMan, EntityUid grid)
    {
        var query = entMan.EntityQueryEnumerator<PowerSupplierComponent, TransformComponent>();
        while (query.MoveNext(out _, out var supplier, out var xform))
        {
            if (xform.GridUid == grid)
                return supplier;
        }

        Assert.Fail("No power supplier on the grid.");
        return default!;
    }
}
