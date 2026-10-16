using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Mono.ArmorPlate;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Inventory;
using Content.Shared.Materials;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Mono;

/// <summary>
/// Palmtree: Monolith armor plates (ported) - plates insert into plate-capable armor, absorb damage,
/// wear out and pass the rest through as stamina damage.
/// </summary>
[TestFixture]
public sealed class ArmorPlateTest
{
    [Test]
    public async Task PlatesAbsorbDamageAndWearOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var damageable = server.System<DamageableSystem>();
            var inventory = server.System<InventorySystem>();
            var stamina = server.System<SharedStaminaSystem>();
            var storage = server.System<SharedStorageSystem>();

            var wearer = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var vest = entMan.SpawnEntity("ClothingOuterArmorBasic", testMap.GridCoords);

            // The standard armor vest and every hardsuit carry the plate holder.
            Assert.That(entMan.HasComponent<ArmorPlateHolderComponent>(vest), Is.True,
                "The armor vest should hold armor plates.");
            var hardsuit = entMan.SpawnEntity("ClothingOuterHardsuitMercenary", testMap.GridCoords);
            Assert.That(entMan.HasComponent<ArmorPlateHolderComponent>(hardsuit), Is.True,
                "Hardsuits should hold armor plates.");
            entMan.DeleteEntity(hardsuit);

            // Storage outerwear (jackets/coats) and the chameleon vest hold plates too.
            var jacket = entMan.SpawnEntity("ClothingOuterCoatBomber", testMap.GridCoords);
            Assert.That(entMan.HasComponent<ArmorPlateHolderComponent>(jacket), Is.True,
                "Storage outerwear (jackets) should hold armor plates.");
            // The plate holder must not replace the jacket's own pocket grid.
            var jacketStorage = entMan.GetComponent<StorageComponent>(jacket);
            Assert.That(jacketStorage.Grid, Is.EqualTo(new List<Box2i> { new(0, 0, 2, 1) }),
                "Jackets keep their own storage grid.");
            var jacketPlate = entMan.SpawnEntity("ArmorPlateBlunt_Slash", testMap.GridCoords);
            Assert.That(storage.Insert(jacket, jacketPlate, out _), Is.True,
                "Armor plates should fit in jacket storage.");
            Assert.That(entMan.GetComponent<ArmorPlateHolderComponent>(jacket).ActivePlate, Is.EqualTo(jacketPlate),
                "A plate in a jacket should become its active plate.");
            entMan.DeleteEntity(jacketPlate);
            entMan.DeleteEntity(jacket);
            var chameleonVest = entMan.SpawnEntity("ClothingOuterChameleon", testMap.GridCoords);
            Assert.That(entMan.HasComponent<ArmorPlateHolderComponent>(chameleonVest), Is.True,
                "The chameleon vest should hold armor plates.");
            entMan.DeleteEntity(chameleonVest);

            // Plates are recyclable through the material reclaimer.
            var protoManager = server.ResolveDependency<IPrototypeManager>();
            foreach (var plateId in new[] { "ArmorPlateBlunt_Slash", "ArmorPlatePierce", "ArmorPlateHeat", "ArmorPlateSpeed" })
            {
                var plateProto = protoManager.Index<EntityPrototype>(plateId);
                Assert.That(plateProto.TryGetComponent<PhysicalCompositionComponent>(out var composition, entMan.ComponentFactory), Is.True,
                    $"{plateId} should have a material composition for the reclaimer.");
                Assert.That(composition!.MaterialComposition.Count, Is.GreaterThan(0),
                    $"{plateId} should return materials when recycled.");
            }
            Assert.That(inventory.TryEquip(wearer, vest, "outerClothing", true, true), Is.True);
            Assert.That(entMan.HasComponent<ArmorPlateProtectedComponent>(wearer), Is.False,
                "No plate installed yet, so the wearer is not plate-protected.");

            // A plate installed into the vest's storage becomes the active plate.
            var plate = entMan.SpawnEntity("ArmorPlatePierce", testMap.GridCoords);
            Assert.That(storage.Insert(vest, plate, out _), Is.True);
            var holder = entMan.GetComponent<ArmorPlateHolderComponent>(vest);
            Assert.That(holder.ActivePlate, Is.EqualTo(plate), "Inserting a plate should activate it.");
            Assert.That(entMan.HasComponent<ArmorPlateProtectedComponent>(wearer), Is.True,
                "An active plate should protect the wearer.");

            // Piercing damage: the ballistic plate absorbs all of it, damages itself and costs stamina.
            var attacker = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            damageable.TryChangeDamage(wearer, new DamageSpecifier(
                server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Piercing"), 20),
                origin: attacker);

            var plateDamage = entMan.GetComponent<DamageableComponent>(plate).TotalDamage.Int();
            Assert.That(plateDamage, Is.GreaterThan(0), "The plate should take durability damage.");
            Assert.That(entMan.GetComponent<DamageableComponent>(wearer).TotalDamage.Int(), Is.EqualTo(0),
                "The plate should absorb the piercing hit entirely.");
            Assert.That(stamina.GetStaminaDamage(wearer), Is.GreaterThan(0f),
                "Absorbed damage should be passed on as stamina damage.");

            // Removing the active plate drops the protection.
            var container = server.System<SharedContainerSystem>();
            Assert.That(container.TryGetContainingContainer(vest, plate, out var plateContainer), Is.True);
            Assert.That(container.Remove(plate, plateContainer), Is.True);
            Assert.That(entMan.HasComponent<ArmorPlateProtectedComponent>(wearer), Is.False,
                "Removing the plate should remove the protection.");

            entMan.DeleteEntity(wearer);
            entMan.DeleteEntity(attacker);
            entMan.DeleteEntity(vest);
            entMan.DeleteEntity(plate);
        });

        await pair.CleanReturnAsync();
    }
}
