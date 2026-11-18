using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Server.Charges;
using Content.Shared.Charges.Components;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Content.Shared.Tiles;
using Content.Shared._CS;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: RCD behaviour added with the ship-construction pass (airlock/firelock stacking,
/// unrestricted admin deconstruction, and matter gain from deconstruction).
/// </summary>
[TestFixture]
public sealed class RcdBehaviorTest
{
    private static void SetMode(IEntityManager entManager, EntityUid rcd, string mode)
    {
        // The RCD component's ProtoId is only writable by RCDSystem; go through its normal message
        // handler instead of writing the component from the test.
        entManager.EventBus.RaiseLocalEvent(rcd, new RCDSystemMessage(mode));
    }

    [Test]
    public async Task AirlockAndFirelockCanShareATile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var plating = new Tile(tileDefMan["Plating"].TileId);

            for (var x = 0; x < 3; x++)
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), plating);

            var tile0 = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var tile1 = new EntityCoordinates(grid.Owner, new Vector2(1.5f, 0.5f));

            var rcd = entManager.SpawnEntity("RCD", tile0);
            var rcdComp = entManager.GetComponent<RCDComponent>(rcd);
            var user0 = entManager.SpawnEntity("MobHuman", tile0);
            var user1 = entManager.SpawnEntity("MobHuman", tile1);

            var tileRef0 = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(0, 0));
            var tileRef1 = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(1, 0));

            // Build an airlock over a firelock.
            entManager.SpawnEntity("Firelock", tile0);
            SetMode(entManager, rcd, "Airlock");
            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef0, new Vector2i(0, 0), null, user0, false),
                Is.True,
                "An airlock should be buildable on top of a firelock.");

            // A second airlock is still refused.
            entManager.SpawnEntity("Airlock", tile0);
            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef0, new Vector2i(0, 0), null, user0, false),
                Is.False,
                "A second airlock should not be buildable on the same tile.");

            // Build a firelock over an airlock.
            entManager.SpawnEntity("Airlock", tile1);
            SetMode(entManager, rcd, "Firelock");
            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef1, new Vector2i(1, 0), null, user1, false),
                Is.True,
                "A firelock should be buildable on top of an airlock.");

            // A second firelock is still refused.
            entManager.SpawnEntity("Firelock", tile1);
            Assert.That(
                rcdSystem.IsRCDOperationStillValid(rcd, rcdComp, grid.Owner, grid.Comp, tileRef1, new Vector2i(1, 0), null, user1, false),
                Is.False,
                "A second firelock should not be buildable on the same tile.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdminRcdDeconstructsAnythingExceptFans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var plating = new Tile(tileDefMan["Plating"].TileId);

            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 0), plating);
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 0), plating);

            var tile0 = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var tile1 = new EntityCoordinates(grid.Owner, new Vector2(1.5f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", tile0);
            var user2 = entManager.SpawnEntity("MobHuman", tile1);

            var plastitanium = entManager.SpawnEntity("WallPlastitanium", tile0);
            var fan = entManager.SpawnEntity("AtmosDeviceFanDirectional", tile1);

            var tileRef0 = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(0, 0));
            var tileRef1 = mapSystem.GetTileRef(grid.Owner, grid.Comp, new Vector2i(1, 0));

            var adminRcd = entManager.SpawnEntity("RCDExperimental", tile0);
            var adminComp = entManager.GetComponent<RCDComponent>(adminRcd);
            SetMode(entManager, adminRcd, "Deconstruct");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(adminRcd, adminComp, grid.Owner, grid.Comp, tileRef0, new Vector2i(0, 0), plastitanium, user, false),
                Is.True,
                "The admin RCD should deconstruct a plastitanium wall.");

            var normalRcd = entManager.SpawnEntity("RCD", tile0);
            var normalComp = entManager.GetComponent<RCDComponent>(normalRcd);
            SetMode(entManager, normalRcd, "Deconstruct");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(normalRcd, normalComp, grid.Owner, grid.Comp, tileRef0, new Vector2i(0, 0), plastitanium, user, false),
                Is.False,
                "The normal RCD should not deconstruct a plastitanium wall.");

            Assert.That(
                rcdSystem.IsRCDOperationStillValid(adminRcd, adminComp, grid.Owner, grid.Comp, tileRef1, new Vector2i(1, 0), fan, user2, false),
                Is.False,
                "Even the admin RCD must not deconstruct directional fans.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeconstructionReturnsMatterAndSpillsTheOverflow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var tileDefMan = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();
        var chargesSystem = entManager.System<ChargesSystem>();

        var addCharges = typeof(RCDSystem).GetMethod("AddDeconstructCharges", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(addCharges, Is.Not.Null, "AddDeconstructCharges helper not found");

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var plating = new Tile(tileDefMan["Plating"].TileId);
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 0), plating);

            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var rcd = entManager.SpawnEntity("RCD", coords);
            var rcdComp = entManager.GetComponent<RCDComponent>(rcd);
            var limited = entManager.GetComponent<LimitedChargesComponent>(rcd);

            // The RCD starts full, so all of the returned matter must spill out.
            addCharges!.Invoke(rcdSystem, new object[] { rcd, rcdComp, 5, coords });

            EntityUid spilled = default;
            var query = entManager.EntityQueryEnumerator<RCDAmmoComponent>();
            while (query.MoveNext(out var ammoUid, out var ammoComp))
            {
                if (ammoComp.Charges == 5)
                    spilled = ammoUid;
            }

            Assert.That(spilled, Is.Not.EqualTo(default(EntityUid)), "A full RCD should drop the gained matter as ammo.");

            // Leave two slots free: two charges go into the tool, three spill out. The spill should
            // top up the partial cartridge already on the tile instead of spawning a new pile.
            chargesSystem.SetCharges((rcd, limited), limited.MaxCharges - 2);
            addCharges.Invoke(rcdSystem, new object[] { rcd, rcdComp, 5, coords });

            Assert.Multiple(() =>
            {
                Assert.That(chargesSystem.GetCurrentCharges((rcd, limited)), Is.EqualTo(limited.MaxCharges));
                Assert.That(
                    entManager.GetComponent<RCDAmmoComponent>(spilled).Charges,
                    Is.EqualTo(8),
                    "Only the matter that does not fit should spill out, merged into the existing partial cartridge.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    [TestCase("RCD", 30)]
    [TestCase("RPD", 60)]
    public async Task OneCompressedMatterCartridgeFillsTheTool(string prototype, int maxCharges)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var chargesSystem = entManager.System<ChargesSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid ammo = default;

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", coords);
            var tool = entManager.SpawnEntity(prototype, coords);
            var limited = entManager.GetComponent<LimitedChargesComponent>(tool);

            Assert.That(limited.MaxCharges, Is.EqualTo(maxCharges));

            chargesSystem.SetCharges((tool, limited), 0);
            ammo = entManager.SpawnEntity("RCDAmmo", coords);

            entManager.EventBus.RaiseLocalEvent(
                ammo,
                new AfterInteractEvent(user, ammo, tool, coords, true));

            Assert.That(
                chargesSystem.GetCurrentCharges((tool, limited)),
                Is.EqualTo(maxCharges),
                "One standard compressed-matter cartridge should fill the tool.");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(ammo), Is.False, "A fully consumed cartridge should be deleted.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RpdUsesOnlyAsMuchMatterAsTheMissingCharges()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var chargesSystem = entManager.System<ChargesSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid ammo = default;

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", coords);
            var rpd = entManager.SpawnEntity("RPD", coords);
            var limited = entManager.GetComponent<LimitedChargesComponent>(rpd);

            chargesSystem.SetCharges((rpd, limited), 30);

            ammo = entManager.SpawnEntity("RCDAmmo", coords);
            entManager.EventBus.RaiseLocalEvent(
                ammo,
                new AfterInteractEvent(user, ammo, rpd, coords, true));

            Assert.Multiple(() =>
            {
                Assert.That(
                    chargesSystem.GetCurrentCharges((rpd, limited)),
                    Is.EqualTo(60),
                    "The RPD should still fill its last 30 charges.");

                Assert.That(
                    entManager.GetComponent<RCDAmmoComponent>(ammo).Charges,
                    Is.EqualTo(15),
                    "Only the 15 matter needed for the last 30 charges should be consumed.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RpdClickActuallyBuildsAPipe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));
            var user = entManager.SpawnEntity("MobHuman", coords);
            var rpd = entManager.SpawnEntity("RPD", coords);
            SetMode(entManager, rpd, "PipeStraight");

            // This is the same event the interaction system raises when the player left-clicks
            // with the RPD. Raising it directly isolates the server handler from the client ghost.
            entManager.EventBus.RaiseLocalEvent(rpd, new AfterInteractEvent(user, rpd, null, coords, true));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            // Only count pipes on this test's own grid. Other tests in the pooled pair can leave
            // pipes on their own maps, and CleanReturnAsync only removes the registered TestMap.
            var pipes = 0;
            var query = entManager.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var xform))
            {
                if (meta.EntityPrototype?.ID == "GasPipeStraight" && xform.GridUid == testMap.Grid.Owner)
                    pipes++;
            }

            Assert.That(pipes, Is.EqualTo(1), "The RPD click handler should have spawned one pipe.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProtectedGridOnlyBlocksNonAdmins()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var rcdSystem = entManager.System<RCDSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            var prot = entManager.AddComponent<ProtectedGridComponent>(grid.Owner);
            typeof(ProtectedGridComponent)
                .GetField(nameof(ProtectedGridComponent.PreventRCDUse))!
                .SetValue(prot, true);

            var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
            var rcd = entManager.SpawnEntity("RCD", coords);
            var comp = entManager.GetComponent<RCDComponent>(rcd);
            var user = entManager.SpawnEntity("MobHuman", coords);

            Assert.That(
                rcdSystem.IsRcdUseAllowedOnGrid(grid.Owner, user, comp),
                Is.False,
                "A normal RCD must stay blocked on a protected grid.");

            var admin = entManager.SpawnEntity("MobHuman", coords);
            entManager.AddComponent<AdminGhostComponent>(admin);
            Assert.That(
                rcdSystem.IsRcdUseAllowedOnGrid(grid.Owner, admin, comp),
                Is.True,
                "An admin ghost must be able to build on a protected grid.");

            var adminRcd = entManager.SpawnEntity("RCDExperimental", coords);
            var adminComp = entManager.GetComponent<RCDComponent>(adminRcd);
            Assert.That(
                rcdSystem.IsRcdUseAllowedOnGrid(grid.Owner, user, adminComp),
                Is.True,
                "The unrestricted admin RCD must be able to build on a protected grid.");
        });

        await pair.CleanReturnAsync();
    }
}
