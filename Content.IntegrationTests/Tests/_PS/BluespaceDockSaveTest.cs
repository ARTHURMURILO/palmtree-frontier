using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.BluespaceDock;
using Content.Server.Botany.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._PS.BluespaceDock;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Climbing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Storage;
using Content.Shared.Timing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.SmartFridge;
using NUnit.Framework;
using Robust.Server.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the bluespace dock's ship files must survive the things players keep on their ships. A
/// live save serializes every component on the grid, so any component holding a field the serializer
/// cannot write failed the whole save - a used plant analyzer (the "last scanned plant" memory) and
/// an in-progress do-after both used to do exactly that.
/// </summary>
[TestFixture]
public sealed class BluespaceDockSaveTest
{
    /// <summary>
    /// Components whose fields cannot be serialized break the save before any file is written, and
    /// the dock cannot store the ship at all.
    /// </summary>
    [Test]
    public async Task GridsWithUsedPlantAnalyzersAndDoAftersStillSave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        var analyzerName = string.Empty;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            // A plant analyzer that has scanned something: PlantInfo is set and the display would
            // otherwise be saved with it.
            var analyzer = entMan.SpawnEntity("PlantAnalyzerEmpty", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            analyzerName = entMan.GetComponent<MetaDataComponent>(analyzer).EntityName;
            var analyzerComp = entMan.GetComponent<PlantAnalyzerComponent>(analyzer);
            analyzerComp.PlantInfo = new()
            {
                TargetEntity = entMan.GetNetEntity(analyzer),
                IsTray = false,
                SeedName = "apples",
                Endurance = 100f,
                Lifespan = 55f,
                Maturation = 4f,
                Production = 6f,
                GrowthStages = 5,
                SeedPotency = 50f,
            };

            // An entity in the middle of a do-after (a climber, a grave being dug, a scan in
            // progress, ...) carries a DoAfterId. (The test map's grid is a single tile, so it has
            // to be spawned on it.)
            var climber = entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var climbing = entMan.EnsureComponent<ClimbingComponent>(climber);
            climbing.DoAfter = new DoAfterId(climber, 1);

            var path = BluespaceDockSystem.GetShipFilePath("plant-analyzer-save-test");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True,
                $"Saving a grid with a used plant analyzer failed: {error}");

            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;

            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);

            EntityUid? loadedAnalyzer = null;
            EntityUid? loadedClimber = null;
            foreach (var uid in entities)
            {
                switch (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                {
                    case "PlantAnalyzerEmpty":
                        loadedAnalyzer = uid;
                        break;
                    case "ToolboxMechanicalFilled":
                        loadedClimber = uid;
                        break;
                }
            }

            Assert.That(loadedAnalyzer, Is.Not.Null,
                $"The loaded ship lost its plant analyzer ({analyzerName}).");

            // The scan memory is runtime UI state: it is not saved, and certainly not a failure.
            Assert.That(entMan.GetComponent<PlantAnalyzerComponent>(loadedAnalyzer!.Value).PlantInfo, Is.Null,
                "The plant analyzer's runtime scan memory should not survive a save.");

            // The in-progress do-after id survives as a normal field: the uid is remapped to the
            // loaded entity (a dangling one must not make the save or the load fail).
            Assert.That(loadedClimber, Is.Not.Null, "The loaded ship lost the climbing toolbox.");
            var loadedClimbing = entMan.GetComponent<ClimbingComponent>(loadedClimber!.Value);
            Assert.That(loadedClimbing.DoAfter, Is.Not.Null, "The do-after id was not read back.");
            Assert.That(loadedClimbing.DoAfter!.Value.Uid, Is.EqualTo(loadedClimber.Value),
                "The do-after id was not remapped to the loaded entity.");
            Assert.That(loadedClimbing.DoAfter.Value.Index, Is.EqualTo(1), "The do-after id's index changed.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A used smart fridge (its UI entry cache maps <c>SmartFridgeEntry</c> - a mapping type, so it
    /// cannot be a dictionary key - to session-scoped <c>NetEntity</c>s) used to fail the whole save
    /// with "Caught exception while serializing component SmartFridge". The cache is not persisted and
    /// is rebuilt from the container on startup instead, so the fridge and its contents survive.
    /// </summary>
    [Test]
    public async Task UsedSmartFridgeSurvivesTheSave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = server.System<ContainerSystem>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var hands = server.System<SharedHandsSystem>();
            var grid = testMap.Grid.Owner;

            var fridge = entMan.SpawnEntity("SmartFridge", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var apple = entMan.SpawnEntity("FoodApple", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            Assert.That(hands.TryForcePickupAnyHand(user, apple), Is.True, "sanity: the user must hold the apple");

            // The real insertion path populates ContainedEntries (the part that could not be saved).
            var interact = new InteractUsingEvent(user, apple, fridge,
                entMan.GetComponent<TransformComponent>(fridge).Coordinates);
            entMan.EventBus.RaiseLocalEvent(fridge, interact);
            Assert.That(interact.Handled, Is.True, "sanity: the apple was not inserted into the fridge");
            Assert.That(entMan.GetComponent<SmartFridgeComponent>(fridge).ContainedEntries.Count, Is.EqualTo(1),
                "sanity: the fridge lists its contents");

            var path = BluespaceDockSystem.GetShipFilePath("smart-fridge-save-test");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True,
                $"Saving a grid with a used smart fridge failed: {error}");
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);

            EntityUid? loadedFridge = null;
            foreach (var uid in entities)
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "SmartFridge")
                    loadedFridge = uid;
            }

            Assert.That(loadedFridge, Is.Not.Null, "The retrieved ship lost its smart fridge.");

            var containers = server.System<ContainerSystem>();
            var fridgeComp = entMan.GetComponent<SmartFridgeComponent>(loadedFridge!.Value);
            Assert.That(containers.TryGetContainer(loadedFridge.Value, fridgeComp.Container, out var container), Is.True);

            var apples = container!.ContainedEntities.Count(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "FoodApple");
            Assert.That(apples, Is.EqualTo(1), "The retrieved fridge lost its contents.");

            // The entry list is rebuilt on startup, so the fridge can still dispense it.
            Assert.That(fridgeComp.ContainedEntries.Count, Is.EqualTo(1),
                "The retrieved fridge's entry list was not rebuilt from its contents.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A PDA carries a <c>StoreComponent</c>, whose <c>StartingMap</c> field holds the map entity it
    /// was created on - a reference to the saved grid's parent, which a grid file cannot carry (the
    /// strict save used to fail on it). It also links its ID card in a runtime field that the loader
    /// never fills, so a retrieved PDA stopped recognizing its own card. Both are restored by the
    /// dock; the card's name/job/access survive, while its ship deed deliberately does not (the dock
    /// registry owns the ship identity).
    /// </summary>
    [Test]
    public async Task PdaAndIdCardSurviveTheSave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = server.System<ContainerSystem>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;
        var beforeTags = string.Empty;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var sharedDock = server.System<SharedBluespaceDockSystem>();
            var idCards = server.System<SharedIdCardSystem>();
            var grid = testMap.Grid.Owner;

            var pda = entMan.SpawnEntity("PassengerPDA", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var pdaComp = entMan.GetComponent<PdaComponent>(pda);
            var card = pdaComp.IdSlot.ContainerSlot?.ContainedEntity;
            Assert.That(card, Is.Not.Null, "sanity: the PDA has no ID card");

            var cardComp = entMan.GetComponent<IdCardComponent>(card!.Value);
            cardComp.FullName = "Probe Person";
            idCards.TryChangeJobTitle(card.Value, "Probe Job");
            beforeTags = string.Join(",", entMan.GetComponent<AccessComponent>(card.Value).Tags);

            // A deed on the card must not survive the store: the registry owns the ship identity.
            sharedDock.AssignDeed(card.Value, grid, "Deed Ship", null, "Deed Owner");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(card.Value), Is.True, "sanity: deed on the card");

            var path = BluespaceDockSystem.GetShipFilePath("pda-save-test");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True,
                $"Saving a grid with a PDA failed: {error}");
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var idCards = server.System<SharedIdCardSystem>();
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);

            EntityUid? loadedPda = null;
            foreach (var uid in entities)
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "PassengerPDA")
                    loadedPda = uid;
            }

            Assert.That(loadedPda, Is.Not.Null, "The retrieved ship lost its PDA.");
            var pdaComp = entMan.GetComponent<PdaComponent>(loadedPda!.Value);

            var card = pdaComp.IdSlot.ContainerSlot?.ContainedEntity;
            Assert.That(card, Is.Not.Null, "The retrieved PDA lost its ID card.");
            Assert.That(pdaComp.PenSlot.ContainerSlot?.ContainedEntity, Is.Not.Null,
                "The retrieved PDA lost its pen.");

            // The card link is runtime state; without it access checks and the ID UI are dead.
            Assert.That(pdaComp.ContainedId, Is.EqualTo(card),
                "The retrieved PDA does not recognize the card in its ID slot.");
            Assert.That(idCards.TryFindIdCard(loadedPda.Value, out var foundCard) && foundCard.Owner == card,
                Is.True, "The retrieved PDA has no findable ID card.");

            // The card's identity and access survive...
            var cardComp = entMan.GetComponent<IdCardComponent>(card!.Value);
            Assert.That(cardComp.FullName, Is.EqualTo("Probe Person"), "The retrieved ID card lost its name.");
            Assert.That(string.Join(",", entMan.GetComponent<AccessComponent>(card.Value).Tags), Is.EqualTo(beforeTags),
                "The retrieved ID card lost its access.");

            // ...but not the deed (it belongs to the dock registry, and the ship it names no longer exists).
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(card.Value), Is.False,
                "A stored ID card came back with its old ship deed.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An update that removes a prototype **inside a container** on a stored ship: the sanitized
    /// copy drops the entity (and its contents), and the containers that named it must not be left
    /// pointing at a uid the copy no longer declares. The deserializer turns those into
    /// <c>EntityUid.Invalid</c> and logs "unknown entity yaml uid" for each one.
    /// </summary>
    [Test]
    public async Task MigratedShipFilesPruneReferencesToRemovedContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();
        var containers = server.System<ContainerSystem>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var grid = testMap.Grid.Owner;

            // A toolbox holding one extra item, so the file has container references to the item.
            var toolbox = entMan.SpawnEntity("ToolboxMechanicalFilled", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var duck = entMan.SpawnEntity("ToyRubberDuck", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            Assert.That(containers.TryGetContainer(toolbox, StorageComponent.ContainerId, out var storage), Is.True,
                "sanity: the toolbox has storage");
            Assert.That(containers.Insert(duck, storage!), Is.True, "sanity: the duck fits in the toolbox");
            var before = storage!.ContainedEntities.Count;

            var file = BluespaceDockSystem.GetShipFilePath("container-migration");
            Assert.That(dock.TrySaveShipFile(grid, file, out var saveError), Is.True, saveError);

            // Pretend the update removed the contained item's prototype.
            var path = new ResPath($"{BluespaceDockSystem.ShipDirectory}/{file}");
            string text;
            using (var reader = res.UserData.OpenText(path))
            {
                text = reader.ReadToEnd();
            }

            Assert.That(text, Does.Contain("proto: ToyRubberDuck"),
                "sanity: the saved file names the contained item's prototype");

            using (var writer = res.UserData.OpenWriteText(path))
            {
                writer.Write(text.Replace("proto: ToyRubberDuck", "proto: PrototypeRemovedByAnUpdate"));
            }

            Assert.That(dock.TryLoadShipFile(file, out var loaded), Is.True,
                "A ship file whose contained item's prototype was removed must still load.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;

            // The toolbox survived without the duck, and nothing in it points at the dropped entity.
            EntityUid? loadedToolbox = null;
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);
            foreach (var uid in entities)
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ToolboxMechanicalFilled")
                    loadedToolbox = uid;
            }

            Assert.That(loadedToolbox, Is.Not.Null, "The toolbox was dropped with the removed prototype.");

            var loadedStorage = entMan.GetComponent<StorageComponent>(loadedToolbox!.Value);
            Assert.That(loadedStorage.StoredItems.Keys.Any(uid => !uid.IsValid()), Is.False,
                "The toolbox kept an invalid entity reference to the removed item.");

            Assert.That(containers.TryGetContainer(loadedToolbox.Value, StorageComponent.ContainerId, out var loadedContainer),
                Is.True);
            Assert.That(loadedContainer!.ContainedEntities.Any(uid => !uid.IsValid()), Is.False,
                "The toolbox's container kept an invalid entity reference to the removed item.");
            Assert.That(loadedContainer.ContainedEntities.Count, Is.EqualTo(before - 1),
                "Only the removed item should be gone from the toolbox.");

            dock.DeleteShipFile(file);
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Action cooldowns are absolute round times (<see cref="UseDelayInfo.EndTime"/> is compared to
    /// <c>IGameTiming.CurTime</c>), and the clock restarts every round. A ship stored late in a round
    /// used to come back with every use-delayed entity - buttons, switches, toolbelts, guns - still
    /// "on cooldown" until the new round reached the old time: clicks and E did nothing while
    /// right-click verbs (which bypass the delay) still worked.
    /// </summary>
    [Test]
    public async Task SavedShipForgetsActionCooldowns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid loadedGrid = default;
        MapId loadedMap = default;
        var stale = TimeSpan.FromHours(1);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var interaction = server.System<SharedInteractionSystem>();
            var useDelaySystem = server.System<UseDelaySystem>();
            var deviceLink = server.System<DeviceLinkSystem>();
            var mapSystem = server.System<SharedMapSystem>();
            var grid = testMap.Grid.Owner;

            // A signal button wired to a light, a toolbelt (E opens its storage) and a gun, so the
            // three use-delay paths a player hits are covered.
            var button = entMan.SpawnEntity("SignalButton", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var light = entMan.SpawnEntity("PoweredSmallLight", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            deviceLink.SaveLinks(null, button, light, new List<(string, string)> { ("Pressed", "Toggle") });

            var belt = entMan.SpawnEntity("ClothingBeltUtilityFilled", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var gun = entMan.SpawnEntity("PSWeaponRifleAk", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            var player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            // Pretend the ship was stored late in a round: every cooldown is far in the future of a
            // freshly restarted round.
            foreach (var uid in new[] { button, belt, gun })
            {
                var useDelay = entMan.EnsureComponent<UseDelayComponent>(uid);
                useDelaySystem.SetLength((uid, useDelay), TimeSpan.FromSeconds(1));
                foreach (var delay in useDelay.Delays.Values)
                {
                    delay.StartTime = stale - TimeSpan.FromSeconds(1);
                    delay.EndTime = stale;
                }
            }

            // NextFire is access-restricted to the gun systems; reflection is only used to fake the
            // stale cooldown a real save would carry.
            typeof(GunComponent)
                .GetField(nameof(GunComponent.NextFire))!
                .SetValue(entMan.GetComponent<GunComponent>(gun), stale);

            Assert.That(useDelaySystem.IsDelayed(button), Is.True, "sanity: the button starts on cooldown");
            Assert.That(interaction.InteractionActivate(player, button), Is.False,
                "sanity: a click on a cooldown-gated button must be dropped (this is the reported bug)");
            Assert.That(interaction.UseInHandInteraction(player, belt), Is.True,
                "sanity: E on a cooldown-gated item is consumed without doing anything");

            var path = BluespaceDockSystem.GetShipFilePath("cooldown-save-test");
            Assert.That(dock.TrySaveShipFile(grid, path, out var error), Is.True, error);
            Assert.That(dock.TryLoadShipFile(path, out var loaded), Is.True, "Loading the saved ship failed.");
            loadedGrid = loaded!.Value;
            loadedMap = entMan.GetComponent<TransformComponent>(loadedGrid).MapID;
            dock.DeleteShipFile(path);
        });

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var interaction = server.System<SharedInteractionSystem>();
            var useDelaySystem = server.System<UseDelaySystem>();

            EntityUid button = default;
            EntityUid belt = default;
            EntityUid gun = default;
            EntityUid player = default;

            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);
            foreach (var uid in entities)
            {
                switch (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                {
                    case "SignalButton":
                        button = uid;
                        break;
                    case "ClothingBeltUtilityFilled":
                        belt = uid;
                        break;
                    case "PSWeaponRifleAk":
                        gun = uid;
                        break;
                }
            }

            var foundProtos = string.Join(",", entities.Select(u => entMan.GetComponent<MetaDataComponent>(u).EntityPrototype?.ID));
            Assert.That(button.IsValid() && belt.IsValid() && gun.IsValid(), Is.True,
                $"The retrieved ship is missing one of the test entities. Found: {foundProtos}");

            // Mobs are not part of a ship file; the interacting player is a fresh one on the grid.
            player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(loadedGrid, new Vector2(0.5f, 0.5f)));

            Assert.That(useDelaySystem.IsDelayed(button), Is.False,
                "The retrieved button is still on a cooldown from the previous round.");
            Assert.That(useDelaySystem.IsDelayed(belt), Is.False,
                "The retrieved toolbelt is still on a cooldown from the previous round.");
            Assert.That(entMan.GetComponent<GunComponent>(gun).NextFire, Is.EqualTo(TimeSpan.Zero),
                "The retrieved gun kept its shot cooldown from the previous round.");

            // The click now goes through and the button's signal reaches the light.
            Assert.That(interaction.InteractionActivate(player, button), Is.True,
                "A retrieved button still refuses clicks.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var entities = new HashSet<EntityUid>();
            dock.GetEntitiesOnGrid(loadedGrid, entities);

            EntityUid? light = null;
            foreach (var uid in entities)
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "PoweredSmallLight")
                    light = uid;
            }

            Assert.That(light, Is.Not.Null, "The retrieved ship lost its light.");
            Assert.That(entMan.GetComponent<PoweredLightComponent>(light!.Value).On, Is.False,
                "The retrieved button's click did not reach the linked light.");
        });

        await server.WaitAssertion(() => entMan.DeleteEntity(loadedGrid));
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(loadedMap));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship that is stored, deleted or sold must not leave radar consoles pointing at it: the
    /// console state refresh logs "Can't resolve MetaDataComponent" for a dead target on every
    /// refresh (the console's own ship arriving through FTL, for instance).
    /// </summary>
    [Test]
    public async Task RadarTargetsDoNotOutliveTheShipTheyPointAt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            var dock = server.System<BluespaceDockSystem>();
            var radarSystem = server.System<RadarConsoleSystem>();
            var grid = testMap.Grid.Owner;

            // A shuttle console watching another grid.
            var console = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var ship = mapMan.CreateGridEntity(testMap.MapId).Owner;

            var radar = entMan.GetComponent<RadarConsoleComponent>(console);
            radarSystem.SetTarget((console, radar), entMan.GetNetEntity(ship), Vector2.Zero);
            Assert.That(radar.TargetEntity, Is.EqualTo(ship), "The console did not take the target.");

            // Deleting the watched ship (the admin ship manager and the dock's sell path) drops the
            // reference right away.
            Assert.That(dock.TryDeleteShip(ship, out var error), Is.True, error);
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(console).TargetEntity, Is.Null,
                "Deleting a ship left a radar console pointing at it.");

            // A target that was deleted without the console's knowledge (an older ship file, an
            // admin delete) is dropped when the console next refreshes its state.
            ship = mapMan.CreateGridEntity(testMap.MapId).Owner;
            radar = entMan.GetComponent<RadarConsoleComponent>(console);
            radarSystem.SetTarget((console, radar), entMan.GetNetEntity(ship), Vector2.Zero);
            Assert.That(radar.TargetEntity, Is.EqualTo(ship), "The console did not take the second target.");
            entMan.DeleteEntity(ship);

            server.System<ShuttleConsoleSystem>().RefreshShuttleConsoles();
            Assert.That(entMan.GetComponent<RadarConsoleComponent>(console).TargetEntity, Is.Null,
                "A console refresh left a radar console pointing at a deleted ship.");
        });

        await pair.CleanReturnAsync();
    }
}
