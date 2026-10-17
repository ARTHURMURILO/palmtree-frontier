using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Light.Components;
using Content.Shared.Projectiles;
using Content.Shared.Singularity.Components;
using Content.Shared.Station;
using Content.Shared.Weapons.Ranged.Components;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: after a ship is stored in the bluespace dock, retrieved and undocked again, the systems
/// on it must keep working - a saved gun still fires with exactly the ammo and magazine it was saved
/// with, a light switched off with a button stays off, and saved emitters (the A.P.E. and the regular
/// emitter) still fire moving bolts.
/// </summary>
[TestFixture]
public sealed class BluespaceDockShipSystemsTest
{
    private const string GunProto = "PSWeaponRifleAk";
    private const string MagazineProto = "NFMagazineRifle30";
    private const string LightProto = "PoweredSmallLight";
    private const string ButtonProto = "SignalButton";
    private const string ApeProto = "MachineAPE";
    private const string EmitterProto = "Emitter";
    private const string ApeBoltProto = "NFAnomalousParticleDelta";
    private const string EmitterBoltProto = "NFEmitterBolt";

    [Test]
    public async Task SavedShipSystemsWorkAfterUndock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var testMap = await pair.CreateTestMap();

        var session = playerMan.Sessions.Single();
        var userId = session.UserId;

        EntityUid playerMob = default;
        EntityUid console = default;
        EntityUid shipGrid = default;
        var shipId = string.Empty;
        var characterName = string.Empty;

        await server.WaitPost(() =>
        {
            playerMob = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.AddComponent<Content.Shared._NF.Bank.Components.BankAccountComponent>(playerMob);
            playerMan.SetAttachedEntity(session, playerMob);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var sharedDock = server.System<SharedBluespaceDockSystem>();
            var stationSystem = server.System<StationSystem>();
            var deviceLink = server.System<DeviceLinkSystem>();
            var docking = server.System<DockingSystem>();
            var gunSystem = server.System<GunSystem>();
            var itemSlots = server.System<ItemSlotsSystem>();
            var mapSystem = server.System<SharedMapSystem>();
            var xformSystem = server.System<SharedTransformSystem>();
            dock.ReloadRegistry();

            // Station grid with the dock console.
            var stationGrid = testMap.Grid.Owner;
            mapSystem.SetTiles(stationGrid, entMan.GetComponent<MapGridComponent>(stationGrid),
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)) });

            stationSystem.InitializeNewStation(
                new StationConfig { StationPrototype = "StandardFrontierVessel" },
                new List<EntityUid> { stationGrid },
                "Diag Station");

            // The shuttle records store lives on the sector service entity.
            var serviceHost = entMan.SpawnEntity(null, new EntityCoordinates(stationGrid, Vector2.Zero));
            entMan.AddComponent<Content.Server._NF.SectorServices.StationSectorServiceHostComponent>(serviceHost);

            console = entMan.SpawnEntity("ComputerBluespaceDock",
                new EntityCoordinates(stationGrid, new Vector2(0.5f, 0.5f)));

            var idCard = entMan.SpawnEntity("PassengerIDCard",
                new EntityCoordinates(stationGrid, new Vector2(0.5f, 0.5f)));
            var consoleComp = entMan.GetComponent<BluespaceDockConsoleComponent>(console);
            Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, idCard, null), Is.True,
                "Could not insert the ID card.");

            // Ship grid, away from the station so projectiles have a clear path. Tiles must be
            // orthogonally adjacent or the grid splits.
            shipGrid = mapMan.CreateGridEntity(testMap.MapId).Owner;
            xformSystem.SetLocalPosition(shipGrid, new Vector2(50f, 0f));
            mapSystem.SetTiles(shipGrid, entMan.GetComponent<MapGridComponent>(shipGrid),
                new List<(Vector2i, Tile)>
                {
                    (new Vector2i(0, 0), new Tile(1)),
                    (new Vector2i(1, 0), new Tile(1)),
                    (new Vector2i(2, 0), new Tile(1)),
                    (new Vector2i(3, 0), new Tile(1)),
                    (new Vector2i(4, 0), new Tile(1)),
                });

            // A magazine gun with exactly 7 rounds, chambered and ready to fire.
            var gun = entMan.SpawnEntity(GunProto, new EntityCoordinates(shipGrid, new Vector2(1.5f, 0.5f)));
            var magazine = entMan.SpawnEntity(MagazineProto, new EntityCoordinates(shipGrid, new Vector2(1.5f, 0.5f)));
            var magazineComp = entMan.GetComponent<BallisticAmmoProviderComponent>(magazine);
            gunSystem.SetBallisticUnspawned((magazine, magazineComp), 7);

            var gunSlots = entMan.GetComponent<ItemSlotsComponent>(gun);
            Assert.That(itemSlots.TryInsert(gun, gunSlots.Slots["gun_magazine"], magazine, null), Is.True,
                "Could not insert the magazine.");
            gunSystem.SetBoltClosed(gun, entMan.GetComponent<ChamberMagazineAmmoProviderComponent>(gun), true);
            Assert.That(GetTotalAmmo(entMan, gun), Is.EqualTo(7), "The gun should start with 7 rounds.");

            // A light and the button that switches it off.
            var light = entMan.SpawnEntity(LightProto, new EntityCoordinates(shipGrid, new Vector2(2.5f, 0.5f)));
            var button = entMan.SpawnEntity(ButtonProto, new EntityCoordinates(shipGrid, new Vector2(2.5f, 0.2f)));
            deviceLink.SaveLinks(null, button, light, new List<(string, string)> { ("Pressed", "Toggle") });
            Assert.That(entMan.GetComponent<PoweredLightComponent>(light).On, Is.True, "The light starts on.");
            deviceLink.InvokePort(button, "Pressed");

            // The A.P.E. and a regular emitter, each on their own tile with a clear firing path.
            entMan.SpawnEntity(ApeProto, new EntityCoordinates(shipGrid, new Vector2(3.5f, 0.5f)));
            entMan.SpawnEntity(EmitterProto, new EntityCoordinates(shipGrid, new Vector2(4.5f, 0.5f)));

            // The ship has its own docking port (it was undocked before being stored, so nothing is
            // attached). There is deliberately no free port on the station: the retrieval then
            // places the ship next to the station instead of FTL-docking it, which keeps the client
            // out of the freshly loaded grid's docking-joint state churn.
            entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(shipGrid, new Vector2(0.5f, 0.5f)));

            sharedDock.AssignDeed(shipGrid, shipGrid, "Diag Ship", null, "Diag Owner");

            var idName = entMan.GetComponent<IdCardComponent>(idCard).FullName;
            characterName = dock.GetCharacterName(userId, idName, playerMob);
            Assert.That(characterName.Length, Is.GreaterThan(0), "No character name resolved.");
        });

        // The button's signal is delivered on the next tick; the light should be off by now.
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var gunSystem = server.System<GunSystem>();

            // Sanity: the setup works before storing.
            EntityUid? preGun = null;
            EntityUid? preLight = null;
            foreach (var uid in FindOnGrid(entMan, dock, shipGrid))
            {
                var proto = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (proto == GunProto)
                    preGun = uid;
                else if (proto == LightProto)
                    preLight = uid;
            }

            Assert.That(preGun, Is.Not.Null, "The gun was not spawned.");
            Assert.That(preLight, Is.Not.Null, "The light was not spawned.");
            Assert.That(entMan.GetComponent<PoweredLightComponent>(preLight!.Value).On, Is.False,
                "The button did not switch the light off before storing.");
            Assert.That(GetTotalAmmo(entMan, preGun!.Value), Is.EqualTo(7), "The gun should still have 7 rounds.");

            // Sanity: the gun fires before storing, leaving 5 in the magazine and 1 chambered.
            FireGun(entMan, gunSystem, preGun.Value, playerMob);
            Assert.That(GetTotalAmmo(entMan, preGun.Value), Is.EqualTo(6),
                "Firing the gun before storing did not consume a round.");

            Assert.That(
                dock.TryStoreShip(shipGrid, userId.ToString(), characterName, 100, payer: null, cost: 0,
                    out var storeError),
                Is.True, storeError);

            // Storing a ship must not keep its in-flight projectiles: the sanity shot is gone (it
            // is not in the saved file and is not left flying at the stored gun).
            Assert.That(CountProjectilesByWeapon(entMan, preGun.Value), Is.EqualTo(0),
                "Storing the ship did not drop its in-flight projectiles.");

            var entry = dock.FindEntry(userId.ToString(), characterName, "Diag Ship");
            Assert.That(entry, Is.Not.Null, "No registry entry after the store.");
            shipId = entry!.ShipId;
        });

        await server.WaitAssertion(() =>
        {
            // Drive the real retrieve message exactly like the console UI does.
            var msg = new BluespaceDockRetrieveMessage(shipId) { Actor = playerMob };
            entMan.EventBus.RaiseLocalEvent(console, msg);
        });

        // Let the retrieval settle and the gun's fire cooldown expire.
        await pair.RunTicksSync(30);

        EntityUid foundGun = default;
        EntityUid foundLight = default;
        EntityUid foundMagazine = default;
        EntityUid foundApe = default;
        EntityUid foundEmitter = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var docking = server.System<DockingSystem>();

            EntityUid? ship = null;
            foreach (var deedShip in dock.GetDeedShips())
            {
                if (deedShip.Name.StartsWith("Diag Ship"))
                    ship = deedShip.Grid;
            }

            Assert.That(ship, Is.Not.Null, "The retrieved ship has no deed; retrieval failed.");
            shipGrid = ship!.Value;

            // Undock the ship, the way a player would before flying away.
            foreach (var shipDockComp in docking.GetDocks(shipGrid).ToList())
            {
                if (shipDockComp.Comp.DockedWith != null)
                    docking.Undock(shipDockComp);
            }

            Assert.That(docking.GetDocks(shipGrid).Any(d => d.Comp.DockedWith != null), Is.False,
                "The ship should be undocked.");

            var retrievedEntities = FindOnGrid(entMan, dock, shipGrid).ToList();
            foreach (var uid in retrievedEntities)
            {
                var proto = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                switch (proto)
                {
                    case GunProto:
                        foundGun = uid;
                        break;
                    case LightProto:
                        foundLight = uid;
                        break;
                    case ApeProto:
                        foundApe = uid;
                        break;
                    case EmitterProto:
                        foundEmitter = uid;
                        break;
                }
            }

            Assert.That(foundGun, Is.Not.EqualTo(default(EntityUid)), "The retrieved ship has no gun.");
            Assert.That(foundLight, Is.Not.EqualTo(default(EntityUid)), "The retrieved ship has no light.");
            Assert.That(foundApe, Is.Not.EqualTo(default(EntityUid)), "The retrieved ship has no A.P.E.");
            Assert.That(foundEmitter, Is.Not.EqualTo(default(EntityUid)), "The retrieved ship has no emitter.");

            // Mid-flight projectiles must never be saved: one would come back frozen in place.
            Assert.That(retrievedEntities.Any(uid => entMan.HasComponent<ProjectileComponent>(uid)), Is.False,
                "The retrieved ship contains projectiles that were saved with it.");

            var slots = entMan.GetComponent<ItemSlotsComponent>(foundGun);
            Assert.That(slots.Slots["gun_magazine"].ContainerSlot?.ContainedEntity, Is.Not.Null,
                "The retrieved gun has no magazine.");
            foundMagazine = slots.Slots["gun_magazine"].ContainerSlot!.ContainedEntity!.Value;
            Assert.That(entMan.GetComponent<MetaDataComponent>(foundMagazine).EntityPrototype?.ID,
                Is.EqualTo(MagazineProto), "The retrieved gun lost its magazine.");

            // The exact ammo count: 5 in the magazine and one chambered.
            Assert.That(GetTotalAmmo(entMan, foundGun), Is.EqualTo(6),
                "The retrieved gun did not keep its exact ammo count.");

            // The light switched off before storing must still be off.
            Assert.That(entMan.GetComponent<PoweredLightComponent>(foundLight).On, Is.False,
                "The light did not stay off after being stored.");

            // Power up both emitters (as a player would, and as power arrival does).
            var emitterSystem = server.System<Content.Server.Singularity.EntitySystems.EmitterSystem>();
            var shortDelay = TimeSpan.FromMilliseconds(50);
            foreach (var uid in new[] { foundApe, foundEmitter })
            {
                var emitterComp = entMan.GetComponent<EmitterComponent>(uid);
                emitterComp.FireBurstDelayMin = shortDelay;
                emitterComp.FireBurstDelayMax = shortDelay;
                emitterComp.FireInterval = shortDelay;
                emitterSystem.SwitchOn(uid, emitterComp);
                emitterSystem.PowerOn(uid, emitterComp);
            }
        });

        // Let the emitters actually fire.
        await pair.RunTicksSync(20);

        EntityUid boltUid = default;
        Vector2 boltPosition = default;

        await server.WaitAssertion(() =>
        {
            var xformSystem = server.System<SharedTransformSystem>();

            Assert.That(CountProjectiles(entMan, ApeBoltProto), Is.GreaterThan(0),
                "The stored A.P.E. did not fire.");
            Assert.That(CountProjectiles(entMan, EmitterBoltProto), Is.GreaterThan(0),
                "The stored emitter did not fire.");

            // Record a bolt to check that it is actually travelling.
            var query = entMan.EntityQueryEnumerator<ProjectileComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID != ApeBoltProto)
                    continue;

                boltUid = uid;
                boltPosition = xformSystem.GetWorldPosition(uid);
                break;
            }

            Assert.That(boltUid, Is.Not.EqualTo(default(EntityUid)), "No A.P.E. bolt to track.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var gunSystem = server.System<GunSystem>();
            var xformSystem = server.System<SharedTransformSystem>();

            // The tracked bolt must still exist and have moved away from where it was.
            Assert.That(entMan.EntityExists(boltUid), Is.True, "The A.P.E. bolt disappeared immediately.");
            var boltXform = entMan.GetComponent<TransformComponent>(boltUid);
            var boltPhysics = entMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(boltUid);
            var moved = (xformSystem.GetWorldPosition(boltUid) - boltPosition).Length();
            Assert.That(moved, Is.GreaterThan(0.05f),
                $"The A.P.E. bolt stayed in place instead of travelling. moved={moved} " +
                $"linVel={boltPhysics.LinearVelocity} body={boltPhysics.BodyType} " +
                $"paused={entMan.GetComponent<MetaDataComponent>(boltUid).EntityPaused} " +
                $"map={boltXform.MapID} parent={boltXform.ParentUid}");

            // The saved gun still fires when used, and consumes exactly one round.
            FireGun(entMan, gunSystem, foundGun, playerMob);
            Assert.That(GetTotalAmmo(entMan, foundGun), Is.EqualTo(5),
                "The retrieved gun did not fire (or consumed the wrong amount of ammo).");

            // Leave no bullet behind referencing a soon-to-be-deleted weapon.
            server.System<BluespaceDockSystem>().DeleteProjectiles(shipGrid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The ammo the gun can fire: everything in its magazine plus a chambered round, if any.
    /// </summary>
    private static int GetTotalAmmo(IEntityManager entMan, EntityUid gun)
    {
        var slots = entMan.GetComponent<ItemSlotsComponent>(gun);
        var count = 0;

        if (slots.Slots["gun_magazine"].ContainerSlot?.ContainedEntity is { } magazine)
        {
            var magComp = entMan.GetComponent<BallisticAmmoProviderComponent>(magazine);
            count += magComp.Count;
        }

        if (slots.Slots["gun_chamber"].ContainerSlot?.ContainedEntity != null)
            count++;

        return count;
    }

    private static void FireGun(IEntityManager entMan, GunSystem gunSystem, EntityUid gun, EntityUid user)
    {
        var gunComp = entMan.GetComponent<GunComponent>(gun);
        var coords = entMan.GetComponent<TransformComponent>(gun).Coordinates.Offset(new Vector2(0f, -1f));
        gunSystem.AttemptShoot(user, gun, gunComp, coords);
    }

    /// <summary>
    /// Counts projectiles fired by a gun.
    /// </summary>
    private static int CountProjectilesByWeapon(IEntityManager entMan, EntityUid gun)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out _, out var projectile))
        {
            if (projectile.Weapon == gun)
                count++;
        }

        return count;
    }

    private static int CountProjectiles(IEntityManager entMan, string proto)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == proto)
                count++;
        }

        return count;
    }

    private static IEnumerable<EntityUid> FindOnGrid(IEntityManager entMan, BluespaceDockSystem dock,
        EntityUid shipGrid)
    {
        var entities = new HashSet<EntityUid>();
        dock.GetEntitiesOnGrid(shipGrid, entities);
        return entities;
    }
}
