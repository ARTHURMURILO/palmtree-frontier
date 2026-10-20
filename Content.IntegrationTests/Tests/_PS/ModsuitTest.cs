using System.Linq;
using Content.Shared._PS.Modsuits;
using Content.Server._PS.Modsuits;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Clothing;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Light.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.IoC;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: basic MODsuit deployment, sealing and retraction.
/// </summary>
[TestFixture]
public sealed class ModsuitTest
{
    [Test]
    public async Task AcquisitionPrototypes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var construction = proto.Index<LatheRecipePackPrototype>("PSModsuitConstruction");
            Assert.That(construction.Recipes, Contains.Item("ModsuitConstructionShellRecipe"));
            Assert.That(construction.Recipes, Contains.Item("ModsuitPlatingStandardRecipe"));

            var modules = proto.Index<LatheRecipePackPrototype>("PSModsuitModules");
            Assert.That(modules.Recipes, Contains.Item("ModsuitModuleStorageRecipe"));
            Assert.That(modules.Recipes, Contains.Item("ModsuitModuleMagbootsRecipe"));

            var baseTech = proto.Index<TechnologyPrototype>("PSModsuitBase");
            Assert.That(baseTech.RecipeUnlocks, Contains.Item("ModsuitConstructionShellRecipe"));
            Assert.That(baseTech.RecipeUnlocks, Contains.Item("ModsuitModuleStorageRecipe"));

            var engineeringTech = proto.Index<TechnologyPrototype>("PSModsuitEngineering");
            Assert.That(engineeringTech.TechnologyPrerequisites, Contains.Item("PSModsuitBase"));
            Assert.That(engineeringTech.RecipeUnlocks, Contains.Item("ModsuitPlatingEngineeringRecipe"));

            var specialPack = proto.Index<LatheRecipePackPrototype>("PSModsuitSpecial");
            Assert.That(specialPack.Recipes, Contains.Item("ModsuitPlatingSyndicateRecipe"));
            Assert.That(specialPack.Recipes, Contains.Item("ModsuitPlatingDebugRecipe"));
            var specialTech = proto.Index<TechnologyPrototype>("PSModsuitSpecial");
            Assert.That(specialTech.TechnologyPrerequisites, Contains.Item("PSModsuitCrewThemes"));
            Assert.That(specialTech.RecipeUnlocks, Contains.Item("ModsuitPlatingSyndicateRecipe"));
            Assert.That(proto.HasIndex<ModsuitThemePrototype>("ModsuitThemeSyndicate"), Is.True);
            Assert.That(proto.HasIndex<ModsuitThemePrototype>("ModsuitThemeLoader"), Is.True);
            Assert.That(proto.HasIndex<ModsuitThemePrototype>("ModsuitThemeChrono"), Is.True);

            var techfab = entMan.SpawnEntity("EngineeringTechFab", map.GridCoords);
            var lathe = entMan.GetComponent<LatheComponent>(techfab);
            Assert.That(lathe.DynamicPacks, Contains.Item("PSModsuitConstruction"));
            Assert.That(lathe.DynamicPacks, Contains.Item("PSModsuitModules"));
            Assert.That(lathe.DynamicPacks, Contains.Item("PSModsuitSpecial"));

            entMan.DeleteEntity(techfab);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConstructShell()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var lookup = server.System<EntityLookupSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var shell = entMan.SpawnEntity("ModsuitConstructionShell", map.GridCoords);

            var core = entMan.SpawnEntity("ModsuitCoreStandard", map.GridCoords);
            var screwdriver = entMan.SpawnEntity("Screwdriver", map.GridCoords);
            var wrench = entMan.SpawnEntity("Wrench", map.GridCoords);
            var plating = entMan.SpawnEntity("ModsuitPlatingEngineering", map.GridCoords);

            var parts = new[]
            {
                entMan.SpawnEntity("ModsuitHelmet", map.GridCoords),
                entMan.SpawnEntity("ModsuitChestplate", map.GridCoords),
                entMan.SpawnEntity("ModsuitGauntlets", map.GridCoords),
                entMan.SpawnEntity("ModsuitBoots", map.GridCoords),
            };

            void Use(EntityUid used)
            {
                entMan.EventBus.RaiseLocalEvent(shell,
                    new InteractUsingEvent(user, used, shell, entMan.GetComponent<TransformComponent>(shell).Coordinates));
            }

            Use(core);
            Use(screwdriver);
            foreach (var part in parts)
                Use(part);
            Use(wrench);
            Use(screwdriver);
            Use(plating);

            var controls = lookup.GetEntitiesInRange<ModsuitControlComponent>(entMan.GetComponent<TransformComponent>(shell).Coordinates, 0.5f, LookupFlags.Uncontained);
            Assert.That(controls, Has.Count.EqualTo(1), "the shell should produce one MODsuit control unit");

            var control = controls.First();
            Assert.That(control.Comp.Theme.Id, Is.EqualTo("ModsuitThemeEngineering"));
            Assert.That(control.Comp.Parts, Has.Count.EqualTo(4));
            Assert.That(entMan.HasComponent<StorageComponent>(control.Owner), Is.True,
                "the finished control unit should install theme inbuilt modules");

            entMan.DeleteEntity(user);
            entMan.DeleteEntity(control.Owner);
            entMan.DeleteEntity(screwdriver);
            entMan.DeleteEntity(wrench);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConstructSpecialTheme()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var lookup = server.System<EntityLookupSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var shell = entMan.SpawnEntity("ModsuitConstructionShell", map.GridCoords);
            var core = entMan.SpawnEntity("ModsuitCoreStandard", map.GridCoords);
            var screwdriver = entMan.SpawnEntity("Screwdriver", map.GridCoords);
            var wrench = entMan.SpawnEntity("Wrench", map.GridCoords);
            var plating = entMan.SpawnEntity("ModsuitPlatingSyndicate", map.GridCoords);
            var parts = new[]
            {
                entMan.SpawnEntity("ModsuitHelmet", map.GridCoords),
                entMan.SpawnEntity("ModsuitChestplate", map.GridCoords),
                entMan.SpawnEntity("ModsuitGauntlets", map.GridCoords),
                entMan.SpawnEntity("ModsuitBoots", map.GridCoords),
            };

            void Use(EntityUid used)
            {
                entMan.EventBus.RaiseLocalEvent(shell,
                    new InteractUsingEvent(user, used, shell, entMan.GetComponent<TransformComponent>(shell).Coordinates));
            }

            Use(core);
            Use(screwdriver);
            foreach (var part in parts)
                Use(part);
            Use(wrench);
            Use(screwdriver);
            Use(plating);

            var controls = lookup.GetEntitiesInRange<ModsuitControlComponent>(
                entMan.GetComponent<TransformComponent>(shell).Coordinates, 0.5f, LookupFlags.Uncontained);
            Assert.That(controls, Has.Count.EqualTo(1));
            var control = controls.First();
            Assert.That(control.Comp.Theme.Id, Is.EqualTo("ModsuitThemeSyndicate"));
            Assert.That(entMan.HasComponent<ArmorComponent>(control.Comp.Parts[ModsuitPartSlot.Chestplate]), Is.True);
            Assert.That(entMan.GetComponent<ClothingComponent>(control.Comp.Parts[ModsuitPartSlot.Helmet]).EquippedState,
                Is.EqualTo("syndicate-helmet"));

            entMan.DeleteEntity(user);
            entMan.DeleteEntity(control.Owner);
            entMan.DeleteEntity(screwdriver);
            entMan.DeleteEntity(wrench);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeploySealRetract()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid human = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();

            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suit = entMan.SpawnEntity("ModsuitControlStandard", map.GridCoords);

            Assert.That(entMan.TryGetComponent<ModsuitControlComponent>(suit, out var suitComp), Is.True);
            Assert.That(suitComp!.Parts, Has.Count.EqualTo(4), "the suit should spawn four parts");
            Assert.That(suitComp.Core, Is.Not.Null, "the suit should spawn with a core");
            Assert.That(suitComp.ToggleActionEntity, Is.Not.Null, "the suit should create its toggle action");
            Assert.That(suitComp.SealActionEntity, Is.Not.Null, "the suit should create its seal action");
            Assert.That(suitComp.UiActionEntity, Is.Not.Null, "the suit should create its UI action");
            Assert.That(entMan.HasComponent<StorageComponent>(suit), Is.True, "the inbuilt storage module should add storage");
            Assert.That(entMan.HasComponent<Robust.Shared.GameObjects.UserInterfaceComponent>(suit), Is.True,
                "the suit should expose its control panel interface");
            Assert.That(suitComp.Modules, Has.Count.EqualTo(1), "the standard suit should start with its storage module");

            Assert.That(inventory.TryEquip(human, suit, "back", silent: true, force: true), Is.True,
                "the suit should be wearable on the back slot");

            entMan.EventBus.RaiseLocalEvent(suit, new ModsuitToggleEvent { Performer = human });
            Assert.That(suitComp.Active, Is.True, "the suit should deploy");
            Assert.That(inventory.TryGetSlotEntity(human, "head", out _), Is.True);
            Assert.That(inventory.TryGetSlotEntity(human, "outerClothing", out _), Is.True);
            Assert.That(inventory.TryGetSlotEntity(human, "gloves", out _), Is.True);
            Assert.That(inventory.TryGetSlotEntity(human, "shoes", out _), Is.True);
            Assert.That(inventory.TryUnequip(human, "head", silent: true, force: false), Is.False,
                "deployed MODsuit parts must not be removable individually");

            foreach (var part in suitComp.Parts.Values)
            {
                Assert.That(entMan.HasComponent<ArmorComponent>(part), Is.True);
                Assert.That(entMan.GetComponent<ArmorComponent>(part).Modifiers, Is.Not.Null,
                    "armor must be initialized so examine verbs cannot null-reference");
                Assert.That(entMan.GetComponent<ClothingComponent>(part).RsiPath,
                    Is.EqualTo("_PS/Modsuits/Clothing/mod_clothing.rsi"));
            }

            var module = entMan.SpawnEntity("ModsuitModuleFlashlight", map.GridCoords);
            var moduleSystem = entMan.System<ModsuitModuleSystem>();
            Assert.That(moduleSystem.TryInstallModule((suit, suitComp), module, silent: true, user: human), Is.True,
                "a module should install while the suit is stowed");
            Assert.That(suitComp.Modules, Has.Count.EqualTo(2));
            Assert.That(entMan.HasComponent<UnpoweredFlashlightComponent>(suit), Is.True,
                "the flashlight module should add a suit light");
            Assert.That(entMan.HasComponent<Robust.Server.GameObjects.PointLightComponent>(suit), Is.True,
                "the flashlight module should add a point light");

            var magbootsModule = entMan.SpawnEntity("ModsuitModuleMagboots", map.GridCoords);
            Assert.That(moduleSystem.TryInstallModule((suit, suitComp), magbootsModule, silent: true, user: human), Is.True);
            Assert.That(suitComp.Modules, Has.Count.EqualTo(3));
            var boots = suitComp.Parts[ModsuitPartSlot.Boots];
            Assert.That(entMan.HasComponent<MagbootsComponent>(boots), Is.True);
            Assert.That(entMan.HasComponent<ItemToggleComponent>(boots), Is.True);
            Assert.That(entMan.HasComponent<ToggleClothingComponent>(boots), Is.True);

            entMan.EventBus.RaiseLocalEvent(suit, new ModsuitSealEvent { Performer = human });
            Assert.That(suitComp.Sealed, Is.True, "the suit should seal");
            Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(human).HiddenLayers,
                Contains.Key(HumanoidVisualLayers.Hair),
                "a sealed helmet should hide the wearer's hair");
            foreach (var part in suitComp.Parts.Values)
            {
                Assert.That(entMan.HasComponent<Content.Server.Atmos.Components.PressureProtectionComponent>(part), Is.True);
                Assert.That(entMan.HasComponent<Content.Server.Temperature.Components.TemperatureProtectionComponent>(part), Is.True);
            }

            entMan.EventBus.RaiseLocalEvent(suit, new ModsuitToggleEvent { Performer = human });
            Assert.That(suitComp.Active, Is.False, "the suit should retract");
            Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(human).HiddenLayers,
                Does.Not.ContainKey(HumanoidVisualLayers.Hair),
                "retracting the suit should restore the wearer's hair");
            Assert.That(inventory.TryGetSlotEntity(human, "head", out _), Is.False);
            Assert.That(inventory.TryGetSlotEntity(human, "outerClothing", out _), Is.False);
            Assert.That(inventory.TryGetSlotEntity(human, "gloves", out _), Is.False);
            Assert.That(inventory.TryGetSlotEntity(human, "shoes", out _), Is.False);

            entMan.DeleteEntity(human);
            entMan.DeleteEntity(suit);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeploysOnDigitigradeSpecies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var rodent = entMan.SpawnEntity("MobRodentia", map.GridCoords);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(rodent);
            humanoid.LegStyle = HumanoidLegStyle.Digitigrade;

            var suit = entMan.SpawnEntity("ModsuitControlStandard", map.GridCoords);
            var control = entMan.GetComponent<ModsuitControlComponent>(suit);
            Assert.That(inventory.TryEquip(rodent, suit, "back", silent: true, force: true), Is.True);

            entMan.EventBus.RaiseLocalEvent(suit, new ModsuitToggleEvent { Performer = rodent });
            Assert.That(control.Active, Is.True, "a digitigrade wearer should deploy a MODsuit");
            Assert.That(inventory.TryGetSlotEntity(rodent, "outerClothing", out _), Is.True);
            Assert.That(inventory.TryGetSlotEntity(rodent, "shoes", out _), Is.True);

            entMan.EventBus.RaiseLocalEvent(suit, new ModsuitToggleEvent { Performer = rodent });
            Assert.That(control.Active, Is.False);

            entMan.DeleteEntity(rodent);
            entMan.DeleteEntity(suit);
        });

        await pair.CleanReturnAsync();
    }
}
