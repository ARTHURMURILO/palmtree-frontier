using System.Collections.Generic;
using System.Numerics;
using Content.Server._PS.ShipCombat;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.Projectiles;
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

            // Drain the sink: the weapon loses power on the next power tick.
            var sink = FindSink(entMan, grid);
            server.System<BatterySystem>().SetCharge(sink, 0f, entMan.GetComponent<BatteryComponent>(sink));
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

            entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
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

            entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
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

            Assert.That(server.System<ShipWeaponSystem>().TryManualFire(console), Is.True,
                "The beam turret should fire.");

            Assert.That(entMan.GetComponent<DamageableComponent>(targetWall).TotalDamage.Int(), Is.GreaterThan(0),
                "The beam should hit the target through our own hull.");
            Assert.That(entMan.GetComponent<DamageableComponent>(ownWall).TotalDamage.Int(), Is.EqualTo(0),
                "The beam must not hit our own hull.");
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

        entMan.SpawnEntity("BluespacePowerSink", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
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

    private static EntityUid FindSink(IEntityManager entMan, EntityUid grid)
    {
        var query = entMan.EntityQueryEnumerator<BluespacePowerSinkComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return uid;
        }

        Assert.Fail("No power sink on the grid.");
        return default;
    }
}
