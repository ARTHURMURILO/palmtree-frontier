using System.Linq;
using Content.Server._PS.Protean;
using Content.Server.Atmos.Components;
using Content.Shared._PS.Modsuits;
using Content.Shared._PS.Synth;
using Content.Shared._PS.Synth.DeadStartupButton;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffectNew;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Robust.Shared.Prototypes;
using Content.Shared._PS.Protean;
using Content.Shared.Inventory;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: Prototype-level Protean fold/unfold loop using the polymorph parked-map model.
/// </summary>
[TestFixture]
public sealed class ProteanTest
{
    [Test]
    public async Task SpeciesSpawnsWithSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var proteanSystem = entMan.System<ProteanSystem>();

            var body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            var backpack = entMan.SpawnEntity("ClothingBackpack", map.GridCoords);
            Assert.That(inventory.TryEquip(body, backpack, "back", silent: true, force: true), Is.True);
            Assert.That(entMan.HasComponent<ProteanComponent>(body), Is.True);
            Assert.That(entMan.HasComponent<SynthComponent>(body), Is.True,
                "Proteans should inherit Synth respiration/metabolism behavior");
            Assert.That(entMan.HasComponent<DeadStartupButtonComponent>(body), Is.True,
                "Proteans should inherit the Synth reboot/repair mechanic");
            Assert.That(entMan.HasComponent<PressureImmunityComponent>(body), Is.True,
                "Proteans should not take pressure damage");
            Assert.That(entMan.HasComponent<Content.Shared.Nutrition.Components.HungerComponent>(body), Is.False);
            Assert.That(entMan.HasComponent<Content.Shared.Nutrition.Components.ThirstComponent>(body), Is.False);
            Assert.That(entMan.HasComponent<Content.Shared.Body.Components.BodyComponent>(body), Is.True);
            var bodySystem = entMan.System<Content.Shared.Body.Systems.SharedBodySystem>();
            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>(
                (body, entMan.GetComponent<Content.Shared.Body.Components.BodyComponent>(body)), out var refactories), Is.True);
            Assert.That(refactories, Is.Not.Empty);

            var suit = proteanSystem.EnsureSuit(body);
            Assert.That(suit, Is.Not.Null);
            var suitUid = suit!.Value;
            var protean = entMan.GetComponent<ProteanComponent>(body);
            Assert.That(protean.Suit, Is.EqualTo(suit));
            Assert.That(protean.FoldActionEntity, Is.Not.Null);
            Assert.That(protean.InterfaceActionEntity, Is.Not.Null);
            Assert.That(server.System<UserInterfaceSystem>().HasUi(body, ProteanUiKey.Key), Is.True);
            Assert.That(inventory.TryGetSlotEntity(body, "back", out var back), Is.True);
            Assert.That(back, Is.EqualTo(suit));

            var suitComp = entMan.GetComponent<ProteanSuitComponent>(suitUid);
            var suitControl = entMan.GetComponent<ModsuitControlComponent>(suitUid);
            Assert.That(suitControl.Core, Is.Not.Null);
            Assert.That(entMan.HasComponent<ProteanCoreComponent>(suitControl.Core), Is.True,
                "the integrated suit should use the refactory-linked Protean core");
            Assert.That(entMan.GetComponent<ModsuitCoreComponent>(suitControl.Core!.Value).MaxCharge,
                Is.EqualTo(10f).Within(0.01f));
            Assert.That(entMan.GetComponent<DamageableComponent>(body).DamageModifierSetId,
                Is.EqualTo("Protean"));
            Assert.That(inventory.TryUnequip(body, "back", silent: true, force: false), Is.False,
                "the integrated suit must not be removable by its Protean owner");

            entMan.EventBus.RaiseLocalEvent(body, new ProteanLockEvent { Performer = body });
            Assert.That(suitComp.Locked, Is.True);

            var other = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suitComp.AllowRemoval = true;
            Assert.That(inventory.TryUnequip(body, "back", silent: true, force: true), Is.True);
            suitComp.AllowRemoval = false;
            Assert.That(inventory.TryEquip(other, suitUid, "back", silent: true, force: true), Is.True);
            Assert.That(inventory.TryUnequip(other, "back", silent: true, force: false), Is.False,
                "a locked Protean suit must not be removable by another wearer");

            entMan.DeleteEntity(other);
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AssimilateAndEject()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var proteanSystem = entMan.System<ProteanSystem>();
            var body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            var suit = proteanSystem.EnsureSuit(body);
            Assert.That(suit, Is.Not.Null);
            var suitUid = suit!.Value;
            var suitComp = entMan.GetComponent<ProteanSuitComponent>(suitUid);

            var target = entMan.SpawnEntity("ModsuitControlEngineering", map.GridCoords);
            var targetControl = entMan.GetComponent<ModsuitControlComponent>(target);
            var moduleSystem = entMan.System<Content.Server._PS.Modsuits.ModsuitModuleSystem>();
            var module = entMan.SpawnEntity("ModsuitModuleFlashlight", map.GridCoords);
            Assert.That(moduleSystem.TryInstallModule((target, targetControl), module, silent: true, user: body), Is.True);

            Assert.That(proteanSystem.TryAssimilateSuit((suitUid, suitComp), target, body), Is.True);
            Assert.That(suitComp.StoredSuit, Is.EqualTo(target));
            Assert.That(entMan.GetComponent<ModsuitControlComponent>(suitUid).Theme.Id,
                Is.EqualTo("ModsuitThemeEngineering"));
            Assert.That(suitComp.AssimilatedModules, Contains.Item(module));
            Assert.That(targetControl.Modules, Does.Not.Contain(module));

            Assert.That(proteanSystem.TryEjectSuit(body), Is.True);
            Assert.That(suitComp.StoredSuit, Is.Null);
            Assert.That(entMan.GetComponent<ModsuitControlComponent>(suitUid).Theme.Id,
                Is.EqualTo("ModsuitThemeStandard"));
            Assert.That(targetControl.Modules, Contains.Item(module));
            Assert.That(entMan.EntityExists(target), Is.True);

            entMan.DeleteEntity(target);
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeciesLossCleansUpIntegratedSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var proteanSystem = entMan.System<ProteanSystem>();
            var body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            var suit = proteanSystem.EnsureSuit(body)!.Value;

            entMan.RemoveComponent<ProteanComponent>(body);

            Assert.That(entMan.HasComponent<ProteanSuitComponent>(suit), Is.False,
                "removing the Protean species marker should detach the integrated suit");
            Assert.That(inventory.TryUnequip(body, "back", silent: true, force: false), Is.True,
                "the detached suit should be removable again");

            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SuitDeletionRestoresBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var proteanSystem = entMan.System<ProteanSystem>();
            body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            suit = proteanSystem.EnsureSuit(body)!.Value;
            Assert.That(proteanSystem.TryFold(body, suit), Is.True);
            entMan.QueueDeleteEntity(suit);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(body), Is.True);
            var protean = entMan.GetComponent<ProteanComponent>(body);
            Assert.That(protean.Folded, Is.False);
            Assert.That(protean.Controller, Is.Null);
            Assert.That(protean.Suit, Is.Null);
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LowPowerAndHeal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var bodySystem = entMan.System<SharedBodySystem>();
            var status = entMan.System<StatusEffectsSystem>();
            var proteanSystem = entMan.System<ProteanSystem>();
            var damageable = entMan.System<DamageableSystem>();
            var body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            var suit = proteanSystem.EnsureSuit(body)!.Value;

            entMan.EventBus.RaiseLocalEvent(body, new ProteanLowPowerEvent { Performer = body });
            Assert.That(status.HasStatusEffect(body, "ProteanLowPowerStatusEffect"), Is.True);

            Assert.That(proteanSystem.TryFold(body, suit), Is.True);

            var protean = entMan.GetComponent<ProteanComponent>(body);
            var controller = protean.Controller!.Value;
            var bodyComp = entMan.GetComponent<BodyComponent>(body);
            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((body, bodyComp), out var refactories), Is.True);
            var refactory = refactories![0];
            damageable.TryChangeDamage(body, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 20));

            Assert.That(proteanSystem.TryHeal(body, controller), Is.True);
            Assert.That(entMan.GetComponent<DamageableComponent>(body).Damage.GetTotal().Float(), Is.EqualTo(0).Within(0.1f));
            Assert.That(refactory.Comp1.Metal, Is.EqualTo(4f).Within(0.01f));

            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissingSupportOrgansPenalties()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            var proteanSystem = entMan.System<ProteanSystem>();
            var bodySystem = entMan.System<SharedBodySystem>();
            body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            proteanSystem.EnsureSuit(body);
            var bodyComp = entMan.GetComponent<BodyComponent>(body);

            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>(
                (body, bodyComp), out var factories), Is.True);
            Assert.That(bodySystem.RemoveOrgan(factories![0].Owner), Is.True);
            entMan.QueueDeleteEntity(factories[0].Owner);

            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanOrchestratorComponent>(
                (body, bodyComp), out var orchestrators), Is.True);
            Assert.That(bodySystem.RemoveOrgan(orchestrators![0].Owner), Is.True);
            entMan.QueueDeleteEntity(orchestrators[0].Owner);
        });

        await pair.RunTicksSync(360);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DamageableComponent>(body).Damage.GetTotal().Float(),
                Is.GreaterThan(0f), "missing refactory should cause degradation damage");
            Assert.That(entMan.System<StatusEffectsSystem>().HasStatusEffect(body,
                "ProteanOrchestratorPenaltyStatusEffect"), Is.True,
                "missing orchestrator should impose its movement penalty");
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoreTracksRefactoryAndRetractsAtZero()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        EntityUid suit = default;
        ModsuitControlComponent control = default!;

        await server.WaitAssertion(() =>
        {
            var proteanSystem = entMan.System<ProteanSystem>();
            var bodySystem = entMan.System<SharedBodySystem>();
            body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            suit = proteanSystem.EnsureSuit(body)!.Value;
            control = entMan.GetComponent<ModsuitControlComponent>(suit);
            var bodyComp = entMan.GetComponent<BodyComponent>(body);
            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>(
                (body, bodyComp), out var refactories), Is.True);
            refactories![0].Comp1.Metal = 3f;
            entMan.Dirty(refactories[0].Owner, refactories[0].Comp1);

            entMan.EventBus.RaiseLocalEvent(suit, new ModsuitToggleEvent { Performer = body });
            Assert.That(control.Active, Is.True);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var core = entMan.GetComponent<ModsuitCoreComponent>(control.Core!.Value);
            Assert.That(core.Charge, Is.EqualTo(3f).Within(0.5f));
            var bodyComp = entMan.GetComponent<BodyComponent>(body);
            Assert.That(entMan.System<SharedBodySystem>().TryGetBodyOrganEntityComps<ProteanRefactoryComponent>(
                (body, bodyComp), out var refactories), Is.True);
            refactories![0].Comp1.Metal = 0f;
            entMan.Dirty(refactories[0].Owner, refactories[0].Comp1);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(control.Active, Is.False, "the suit should retract when refactory metal reaches zero");
            Assert.That(entMan.GetComponent<ModsuitCoreComponent>(control.Core!.Value).Charge, Is.LessThan(0.1f));
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AssimilatesPlatingTheme()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var proteanSystem = entMan.System<ProteanSystem>();
            var body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            var suit = proteanSystem.EnsureSuit(body)!.Value;
            var suitComp = entMan.GetComponent<ProteanSuitComponent>(suit);
            var plating = entMan.SpawnEntity("ModsuitPlatingEngineering", map.GridCoords);

            Assert.That(proteanSystem.TryAssimilatePlating((suit, suitComp), plating, body), Is.True);
            Assert.That(entMan.GetComponent<ModsuitControlComponent>(suit).Theme.Id,
                Is.EqualTo("ModsuitThemeEngineering"));
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HealRegrowsMissingPartsAndOrgans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var bodySystem = entMan.System<SharedBodySystem>();
            var containerSystem = entMan.System<SharedContainerSystem>();
            var proteanSystem = entMan.System<ProteanSystem>();
            var body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            var suit = proteanSystem.EnsureSuit(body)!.Value;
            Assert.That(proteanSystem.TryFold(body, suit), Is.True);

            var bodyComp = entMan.GetComponent<BodyComponent>(body);
            var head = bodySystem.GetBodyChildrenOfType(body, BodyPartType.Head, bodyComp).First();
            string? EyePrototype(EntityUid organ)
                => entMan.GetComponent<MetaDataComponent>(organ).EntityPrototype?.ID;
            var eye = bodySystem.GetPartOrgans(head.Id)
                .FirstOrDefault(organ => EyePrototype(organ.Id)?.Contains("Eyes") == true);
            Assert.That(eye.Id, Is.Not.EqualTo(default(EntityUid)));
            Assert.That(bodySystem.RemoveOrgan(eye.Id), Is.True);
            entMan.QueueDeleteEntity(eye.Id);

            var rightArm = bodySystem.GetBodyChildrenOfType(body, BodyPartType.Arm, bodyComp)
                .FirstOrDefault(part => part.Component.Symmetry == BodyPartSymmetry.Right);
            Assert.That(rightArm.Id, Is.Not.EqualTo(default(EntityUid)));
            var parent = bodySystem.GetParentPartAndSlotOrNull(rightArm.Id);
            Assert.That(parent, Is.Not.Null);
            Assert.That(containerSystem.TryGetContainer(parent!.Value.Parent,
                SharedBodySystem.GetPartSlotContainerId(parent.Value.Slot), out var armContainer), Is.True);
            Assert.That(containerSystem.Remove(rightArm.Id, armContainer, force: true), Is.True);
            entMan.QueueDeleteEntity(rightArm.Id);

            Assert.That(proteanSystem.TryHeal(body, body), Is.True);
            Assert.That(bodySystem.GetPartOrgans(head.Id)
                .Any(organ => EyePrototype(organ.Id)?.Contains("Eyes") == true), Is.True);
            Assert.That(bodySystem.GetBodyChildrenOfType(body, BodyPartType.Arm, bodyComp)
                .Any(part => part.Component.Symmetry == BodyPartSymmetry.Right), Is.True);

            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CriticalFoldRepairAndUnfold()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var bodySystem = entMan.System<SharedBodySystem>();
            var mobState = entMan.System<MobStateSystem>();
            var proteanSystem = entMan.System<ProteanSystem>();

            body = entMan.SpawnEntity("MobProtean", map.GridCoords);
            suit = proteanSystem.EnsureSuit(body)!.Value;
            var bodyComp = entMan.GetComponent<BodyComponent>(body);
            var mobStateComp = entMan.GetComponent<MobStateComponent>(body);

            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanBrainComponent>((body, bodyComp), out var brains), Is.True);
            var brain = brains![0];
            brain.Comp1.RepairDuration = TimeSpan.FromSeconds(1);

            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((body, bodyComp), out var refactories), Is.True);
            var oldRefactory = refactories![0];

            mobState.ChangeMobState(body, MobState.Critical, mobStateComp);

            var protean = entMan.GetComponent<ProteanComponent>(body);
            Assert.That(protean.Folded, Is.True, "critical damage should force the Protean into its suit");
            Assert.That(brain.Comp1.Dead, Is.True);
            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((body, bodyComp), out _), Is.False,
                "critical withdrawal should remove the refactory from the body");
            Assert.That(entMan.GetComponent<ModsuitControlComponent>(suit).Open, Is.True);
        });

        await pair.RunTicksSync(45);

        await server.WaitAssertion(() =>
        {
            var bodySystem = entMan.System<SharedBodySystem>();
            var mobState = entMan.System<MobStateSystem>();
            var proteanSystem = entMan.System<ProteanSystem>();
            var bodyComp = entMan.GetComponent<BodyComponent>(body);
            var protean = entMan.GetComponent<ProteanComponent>(body);
            var controller = protean.Controller!.Value;
            var suitComp = entMan.GetComponent<ProteanSuitComponent>(suit);

            var refactory = entMan.SpawnEntity("OrganProteanStomach", map.GridCoords);
            Assert.That(proteanSystem.TryRepair((suit, suitComp), refactory, body), Is.True);
            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((body, bodyComp), out _), Is.True);
            Assert.That(proteanSystem.TryUnfold(controller), Is.False, "repair is still in progress");

            // The repair timer is intentionally shortened in this test to one second.
            // The second tick block below validates the finished state.
        });

        await pair.RunTicksSync(45);

        await server.WaitAssertion(() =>
        {
            var bodySystem = entMan.System<SharedBodySystem>();
            var proteanSystem = entMan.System<ProteanSystem>();
            var bodyComp = entMan.GetComponent<BodyComponent>(body);
            var protean = entMan.GetComponent<ProteanComponent>(body);
            var controller = protean.Controller!.Value;

            Assert.That(bodySystem.TryGetBodyOrganEntityComps<ProteanBrainComponent>((body, bodyComp), out var brains), Is.True);
            Assert.That(brains![0].Comp1.Dead, Is.False);
            Assert.That(proteanSystem.TryUnfold(controller), Is.True);
            Assert.That(protean.Folded, Is.False);

            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FoldAndUnfold()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var proteanSystem = entMan.System<ProteanSystem>();

            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suit = entMan.SpawnEntity("ModsuitControlProtean", map.GridCoords);

            var protean = entMan.AddComponent<ProteanComponent>(body);
            protean.Suit = suit;

            Assert.That(inventory.TryEquip(body, suit, "back", silent: true, force: true), Is.True);

            Assert.That(proteanSystem.TryFold(body, suit), Is.True, "Protean should fold into the suit");
            Assert.That(protean.Folded, Is.True);
            Assert.That(protean.Controller, Is.Not.Null);

            var controller = protean.Controller!.Value;
            Assert.That(entMan.HasComponent<ProteanControllerComponent>(controller), Is.True);
            Assert.That(entMan.GetComponent<ProteanControllerComponent>(controller).OriginalBody, Is.EqualTo(body));
            Assert.That(entMan.GetComponent<ProteanControllerComponent>(controller).Suit, Is.EqualTo(suit));
            Assert.That(entMan.GetComponent<TransformComponent>(controller).ParentUid, Is.EqualTo(suit),
                "the controller should follow the suit");
            Assert.That(entMan.GetComponent<ProteanControllerComponent>(controller).InterfaceActionEntity, Is.Not.Null);
            Assert.That(server.System<UserInterfaceSystem>().HasUi(controller, ProteanUiKey.Key), Is.True);

            Assert.That(proteanSystem.TryUnfold(controller), Is.True, "Protean should unfold from the suit");
            Assert.That(protean.Folded, Is.False);
            Assert.That(protean.Controller, Is.Null);
            Assert.That(inventory.TryGetSlotEntity(body, "back", out var back), Is.True);
            Assert.That(back, Is.EqualTo(suit), "the suit should be re-equipped after unfolding");

            entMan.DeleteEntity(body);
            entMan.DeleteEntity(suit);
        });

        await pair.CleanReturnAsync();
    }
}
