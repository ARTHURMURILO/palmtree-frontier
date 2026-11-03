using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Server.Charges;
using Content.Shared.Charges.Components;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
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

            var ammo = new List<int>();
            var query = entManager.EntityQueryEnumerator<RCDAmmoComponent>();
            while (query.MoveNext(out _, out var ammoComp))
                ammo.Add(ammoComp.Charges);

            Assert.Multiple(() =>
            {
                Assert.That(chargesSystem.GetCurrentCharges((rcd, limited)), Is.EqualTo(limited.MaxCharges));
                Assert.That(ammo, Does.Contain(5), "A full RCD should drop the gained matter as ammo.");
            });

            // Leave two slots free: two charges go into the tool, three spill out.
            chargesSystem.SetCharges((rcd, limited), limited.MaxCharges - 2);
            addCharges.Invoke(rcdSystem, new object[] { rcd, rcdComp, 5, coords });

            ammo.Clear();
            query = entManager.EntityQueryEnumerator<RCDAmmoComponent>();
            while (query.MoveNext(out _, out var ammoComp))
                ammo.Add(ammoComp.Charges);

            Assert.Multiple(() =>
            {
                Assert.That(chargesSystem.GetCurrentCharges((rcd, limited)), Is.EqualTo(limited.MaxCharges));
                Assert.That(ammo, Does.Contain(3), "Only the matter that does not fit should spill out.");
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
}
