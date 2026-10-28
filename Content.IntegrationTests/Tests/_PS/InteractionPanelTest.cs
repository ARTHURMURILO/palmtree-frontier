using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server._PS.Interactions;
using Content.Server._PS.Organs;
using Content.Server.Decals;
using Content.Server.Preferences.Managers; // Palmtree: NSFW age gate
using Content.Shared._PS.Consent; // Palmtree: NSFW age gate
using Content.Shared._PS.Interactions;
using Content.Shared._PS.Organs;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: server-side checks for the ported interaction panel (organ requirements, consent,
/// cooldowns, lust and cum plumbing).
/// </summary>
[TestFixture]
public sealed class InteractionPanelTest
{
    [Test]
    public async Task RequirementsConsentAndCooldown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var appearance = server.System<SharedHumanoidAppearanceSystem>();
            var genitals = server.System<GenitalSystem>();
            var organSystem = server.System<GenitalOrganSystem>();
            var interactions = server.System<InteractionPanelSystem>();

            var user = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var target = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(1f, 0f)));

            // Configure organs through the character profile (the player-facing path).
            var userSettings = new GenitalOrganSettings();
            userSettings.Set(GenitalType.Penis, new GenitalOrganData { Prototype = "PenisHuman", Size = 3 });

            var targetSettings = new GenitalOrganSettings();
            targetSettings.Set(GenitalType.Vagina, new GenitalOrganData { Prototype = "VaginaHuman", Size = 1 });

            appearance.LoadProfile(user, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(userSettings));
            appearance.LoadProfile(target, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(targetSettings));

            Assert.That(organSystem.TryGetOrgan(user, GenitalType.Penis, out _, out _), Is.True);
            Assert.That(organSystem.TryGetOrgan(target, GenitalType.Vagina, out _, out _), Is.True);

            Assert.That(genitals.GetExposure(user, GenitalType.Penis), Is.EqualTo(GenitalExposure.Exposed));
            Assert.That(genitals.GetExposure(target, GenitalType.Vagina), Is.EqualTo(GenitalExposure.Exposed));

            var fuck = proto.Index<InteractionPrototype>("Fuck");
            Assert.That(interactions.CanPerform(fuck, user, target, false), Is.True);

            // Consent gate (session toggle, defaults on).
            interactions.EnsureState(target).Consent = false;
            Assert.That(interactions.CanPerform(fuck, user, target, false), Is.False);
            interactions.EnsureState(target).Consent = true;

            // Clothing gate: wearing a jumpsuit hides the organs (default: HiddenByJumpsuit).
            var jumpsuit = entMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", testMap.GridCoords);
            var inventory = server.System<Content.Shared.Inventory.InventorySystem>();
            Assert.That(inventory.TryEquip(target, jumpsuit, "jumpsuit", true, true), Is.True);
            Assert.That(interactions.CanPerform(fuck, user, target, false), Is.False);
            inventory.TryUnequip(target, "jumpsuit", out _, true, true);

            // Visibility rules (SPLURT/Sandstorm port).
            var userHumanoid = entMan.GetComponent<HumanoidAppearanceComponent>(user);
            var targetHumanoid = entMan.GetComponent<HumanoidAppearanceComponent>(target);

            // Never hidden: exposed even in a jumpsuit.
            organSystem.SetVisibility(user, GenitalType.Penis, GenitalVisibility.NeverHidden);
            Assert.That(genitals.GetExposure(user, GenitalType.Penis), Is.EqualTo(GenitalExposure.Exposed));
            inventory.TryEquip(user, jumpsuit, "jumpsuit", true, true);
            Assert.That(genitals.GetExposure(user, GenitalType.Penis), Is.EqualTo(GenitalExposure.Exposed));
            inventory.TryUnequip(user, "jumpsuit", out _, true, true);

            // Always hidden: never exposed.
            organSystem.SetVisibility(user, GenitalType.Penis, GenitalVisibility.AlwaysHidden);
            Assert.That(genitals.GetExposure(user, GenitalType.Penis), Is.EqualTo(GenitalExposure.Unexposed));

            // Hidden by underwear: exposed bare, hidden with a visible undergarment.
            organSystem.SetVisibility(target, GenitalType.Vagina, GenitalVisibility.HiddenByUnderwear);
            Assert.That(genitals.GetExposure(target, GenitalType.Vagina), Is.EqualTo(GenitalExposure.Exposed));
            targetHumanoid.MarkingSet.AddBack(MarkingCategories.UndergarmentBottom,
                new Marking("TestUndergarment", new List<Color> { Color.White }) { Visible = true });
            Assert.That(genitals.GetExposure(target, GenitalType.Vagina), Is.EqualTo(GenitalExposure.Unexposed));

            // Back to the default rules for the interaction below.
            organSystem.SetVisibility(user, GenitalType.Penis, GenitalVisibility.HiddenByJumpsuit);
            organSystem.SetVisibility(target, GenitalType.Vagina, GenitalVisibility.HiddenByJumpsuit);
            targetHumanoid.MarkingSet.RemoveCategory(MarkingCategories.UndergarmentBottom);

            // Perform, gain lust, then verify the cooldown blocks an immediate repeat.
            Assert.That(interactions.TryPerform(user, target, fuck), Is.True);
            Assert.That(interactions.GetLust(user, interactions.EnsureState(user)), Is.GreaterThan(0));
            Assert.That(interactions.TryPerform(user, target, fuck), Is.False,
                "The interaction cooldown should block an immediate repeat.");

            // Force a climax inside the target's vagina and verify the semen reservoir fills.
            var userState = interactions.EnsureState(user);
            interactions.SetLust(user, userState, userState.LustTolerance * 3 + 1);
            userState.LastInteractionTime = TimeSpan.MinValue;
            Assert.That(interactions.TryPerform(user, target, fuck), Is.True);
            Assert.That(server.System<DrippingCumSystem>().GetSemen(target), Is.GreaterThan(0));

            // Bound UI state and messages must be NetSerializable; serialize them to catch
            // missing attributes/fields at test time instead of at runtime.
            var serializer = server.ResolveDependency<IRobustSerializer>();
            var state = interactions.BuildState(user, interactions.EnsureState(user));
            state.Target = entMan.GetNetEntity(target);

            Serialize(serializer, state);
            Serialize(serializer, new InteractionSelectedMessage("Kiss"));
            Serialize(serializer, new InteractionToggleAutoMessage("Kiss"));
            Serialize(serializer, new InteractionSetPaceMessage(1f));
            Serialize(serializer, new InteractionToggleFavoriteMessage("Kiss"));
            Serialize(serializer, new InteractionGenitalArouseMessage(GenitalType.Penis, true));
            Serialize(serializer, new InteractionGenitalSetVisibilityMessage(GenitalType.Penis, GenitalVisibility.NeverHidden));
            Serialize(serializer, new InteractionSetConsentMessage(false));
            Serialize(serializer, new InteractionSetSoundsMessage(false));
            Serialize(serializer, new InteractionSetArousalMultiplierMessage(true, 150));
            Serialize(serializer, new InteractionSetMoaningMultiplierMessage(true, 50));

            entMan.DeleteEntity(user);
            entMan.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Palmtree: the NSFW age gate. Accounts are treated as non-consenting until they answer the
    /// consent prompt; only the account flag (not just the session toggle) decides. Non-sexual
    /// entries (kissing) skip the account gate, and mobs without a session keep the legacy
    /// behavior so NPCs are unaffected.
    /// </summary>
    [Test]
    public async Task AgeGateGatesSexualInteractions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            Dirty = true,
        });

        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var session = pair.Player;
        Assert.That(session, Is.Not.Null, "No server session for the connected client.");

        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var playerMan = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();

        EntityUid actor = default;
        EntityUid target = default;

        await server.WaitAssertion(() =>
        {
            var appearance = server.System<SharedHumanoidAppearanceSystem>();

            var actorSettings = new GenitalOrganSettings();
            actorSettings.Set(GenitalType.Penis, new GenitalOrganData { Prototype = "PenisHuman", Size = 3 });

            var targetSettings = new GenitalOrganSettings();
            targetSettings.Set(GenitalType.Vagina, new GenitalOrganData { Prototype = "VaginaHuman", Size = 1 });

            actor = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            target = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(1f, 0f)));

            appearance.LoadProfile(actor, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(actorSettings));
            appearance.LoadProfile(target, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(targetSettings));
        });

        // The account flag gates the acting player: a fresh (unanswered) account cannot perform
        // sexual interactions but can still perform non-sexual ones.
        await server.WaitPost(() => playerMan.SetAttachedEntity(session!, actor));

        await server.WaitAssertion(() =>
        {
            var interactions = server.System<InteractionPanelSystem>();
            var prototype = server.ResolveDependency<IPrototypeManager>();
            var fuck = prototype.Index<InteractionPrototype>("Fuck");
            var kiss = prototype.Index<InteractionPrototype>("Kiss");

            Assert.That(interactions.IsNsfwAllowed(actor), Is.False,
                "An unanswered account must count as non-consenting.");
            Assert.That(interactions.CanPerform(fuck, actor, target, false), Is.False,
                "Sexual interactions must be blocked for a non-consenting actor.");
            Assert.That(interactions.CanPerform(kiss, actor, target, false), Is.True,
                "Non-sexual interactions are exempt from the account gate.");
        });

        // Answer the prompt (this is the path the console command takes), then the same
        // interaction passes.
        await server.WaitPost(() => prefs.SetNsfwConsentAsync(session!.UserId, NsfwConsentState.Allowed).Wait());

        await server.WaitAssertion(() =>
        {
            var interactions = server.System<InteractionPanelSystem>();
            var prototype = server.ResolveDependency<IPrototypeManager>();
            var fuck = prototype.Index<InteractionPrototype>("Fuck");

            Assert.That(interactions.IsNsfwAllowed(actor), Is.True);
            Assert.That(interactions.CanPerform(fuck, actor, target, false), Is.True,
                "A consenting actor may perform sexual interactions on a consenting target.");
        });

        // The flag also protects a non-consenting target.
        await server.WaitPost(() => playerMan.SetAttachedEntity(session!, target));

        await server.WaitPost(() => prefs.SetNsfwConsentAsync(session!.UserId, NsfwConsentState.Denied).Wait());

        await server.WaitAssertion(() =>
        {
            var interactions = server.System<InteractionPanelSystem>();
            var prototype = server.ResolveDependency<IPrototypeManager>();
            var fuck = prototype.Index<InteractionPrototype>("Fuck");
            var kiss = prototype.Index<InteractionPrototype>("Kiss");

            Assert.That(interactions.IsNsfwAllowed(target), Is.False);
            Assert.That(interactions.CanPerform(fuck, actor, target, false), Is.False,
                "A non-consenting target must not be subjected to sexual interactions.");
            Assert.That(interactions.CanPerform(kiss, actor, target, false), Is.True,
                "Non-sexual interactions are exempt from the account gate.");
        });

        // Restore the fresh-account state for the pooled server.
        await server.WaitPost(() => prefs.SetNsfwConsentAsync(session!.UserId, NsfwConsentState.Unanswered).Wait());

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClimaxInsideVaginaStartsDrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid receiver = default;

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            receiver = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));

            drip.AddSemen(receiver, 20); // two 10u drips
            Assert.That(drip.GetSemen(receiver), Is.EqualTo(20));
        });

        // Stored semen only starts dripping once the mob moves or acts: an idle mob keeps everything inside.
        await server.WaitRunTicks(70);

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            Assert.That(drip.GetSemen(receiver), Is.EqualTo(20),
                "An idle mob must not drip.");
        });

        // A brief movement starts the drip...
        await PokeMovement(server, receiver, 10);

        // ...and it must keep running while the mob stands still, 10 units per second.
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            var xform = entMan.GetComponent<TransformComponent>(receiver);
            Assert.That(xform.GridUid, Is.Not.Null, "The receiver should be standing on a grid.");

            // Decals are placed where the mob stands (plus scatter), so query around their position.
            var query = xform.LocalPosition; // decals are centered on the mob now
            var decals = server.System<DecalSystem>()
                .GetDecalsInRange(xform.GridUid!.Value, query, 1f)
                .ToList();

            Assert.That(decals.Count(d => d.Decal.Id.StartsWith("SemenDrip")), Is.GreaterThanOrEqualTo(2),
                "The drip must keep running while the mob stands still after it started.");
            Assert.That(decals.Any(d => d.Decal.Id.StartsWith("SemenPuddle")), Is.False,
                "Drips must never escalate to the full cum decals.");
            Assert.That(drip.GetSemen(receiver), Is.EqualTo(0),
                "Two 10u drips should drain the 20u reservoir.");

            entMan.DeleteEntity(receiver);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Palmtree: each drip consumes 10u of stored semen.
    /// </summary>
    [Test]
    public async Task DripConsumesTenUnits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            drip.AddSemen(mob, 25);

            // Start the drip without waiting for a movement tick so exactly one drip is due.
            var comp = entMan.GetComponent<DrippingCumComponent>(mob);
            comp.Ready = true;
            comp.NextDrip = timing.CurTime;
            comp.LastPosition = entMan.GetComponent<TransformComponent>(mob).LocalPosition;
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            Assert.That(drip.GetSemen(mob), Is.EqualTo(15),
                "One drip should consume exactly 10 units.");

            var xform = entMan.GetComponent<TransformComponent>(mob);
            var decals = server.System<DecalSystem>()
                .GetDecalsInRange(xform.GridUid!.Value, xform.LocalPosition, 1f)
                .Count(d => d.Decal.Id.StartsWith("SemenDrip"));
            Assert.That(decals, Is.EqualTo(1), "Only one drip should have happened in two ticks.");

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DripPausesOnAFullTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            var decals = server.System<DecalSystem>();

            mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            drip.AddSemen(mob, 20);

            // Twenty drips already cover the tile the mob is standing on.
            var grid = entMan.GetComponent<TransformComponent>(mob).GridUid!.Value;
            for (var i = 0; i < 20; i++)
            {
                Assert.That(decals.TryAddDecal("SemenDrip1",
                    new EntityCoordinates(grid, new Vector2(0.05f + (i % 5) * 0.09f, 0.05f + (i / 5) * 0.09f)),
                    out _, cleanable: true), Is.True);
            }
        });

        // Move in place for a few seconds: the drip must stay paused and keep every unit stored.
        await PokeMovement(server, mob, 60);

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            var decals = server.System<DecalSystem>();
            var xform = entMan.GetComponent<TransformComponent>(mob);
            var grid = xform.GridUid!.Value;

            Assert.That(drip.GetSemen(mob), Is.EqualTo(20),
                "A full tile must not consume stored semen while the drip is paused.");
            Assert.That(CountDripsOnTile(decals, grid, xform.LocalPosition), Is.EqualTo(20),
                "No new drips may be placed on a tile that already has twenty.");

            // Clean up; the next drip check finds room again and resumes without losing fluid.
            foreach (var (index, _) in decals.GetDecalsInRange(grid, xform.LocalPosition, 1f).ToList())
                decals.RemoveDecal(grid, index);
        });

        await PokeMovement(server, mob, 80);

        await server.WaitAssertion(() =>
        {
            var drip = server.System<DrippingCumSystem>();
            var decals = server.System<DecalSystem>();
            var xform = entMan.GetComponent<TransformComponent>(mob);

            Assert.That(drip.GetSemen(mob), Is.LessThan(20), "The drip should resume once the tile is clear.");
            Assert.That(CountDripsOnTile(decals, xform.GridUid!.Value, xform.LocalPosition), Is.GreaterThan(0));

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WombCapacityAndOverflow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var appearance = server.System<SharedHumanoidAppearanceSystem>();
            var interactions = server.System<InteractionPanelSystem>();
            var drip = server.System<DrippingCumSystem>();
            var decals = server.System<DecalSystem>();

            var user = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var target = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.25f, 0.5f)));

            var userSettings = new GenitalOrganSettings();
            userSettings.Set(GenitalType.Penis, new GenitalOrganData { Prototype = "PenisHuman", Size = 3 });

            var targetSettings = new GenitalOrganSettings();
            targetSettings.Set(GenitalType.Vagina, new GenitalOrganData { Prototype = "VaginaHuman", Size = 1 });

            appearance.LoadProfile(user, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(userSettings));
            appearance.LoadProfile(target, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(targetSettings));

            var grid = testMap.Grid.Owner;
            var fuck = proto.Index<InteractionPrototype>("Fuck");
            var userState = interactions.EnsureState(user);

            // Below the 60u overflow threshold: the 30u pulse is stored and nothing is left behind.
            drip.AddSemen(target, 30);
            Assert.That(drip.GetSemen(target), Is.EqualTo(30));

            interactions.SetLust(user, userState, userState.LustTolerance * 3 + 1);
            Assert.That(interactions.TryPerform(user, target, fuck), Is.True);
            Assert.That(drip.GetSemen(target), Is.EqualTo(60), "The 30u pulse is always stored.");
            Assert.That(decals.GetDecalsInRange(grid, entMan.GetComponent<TransformComponent>(target).LocalPosition, 2f)
                .Any(d => d.Decal.Id.StartsWith("SemenPuddle")), Is.False,
                "A womb at or below the overflow threshold must not leave overflow decals.");

            // Above the threshold: the same pulse overflows onto the floor and is still stored.
            drip.AddSemen(target, 40); // 60 -> 100
            userState.LastInteractionTime = TimeSpan.MinValue;
            interactions.SetLust(user, userState, userState.LustTolerance * 3 + 1);
            Assert.That(interactions.TryPerform(user, target, fuck), Is.True);
            Assert.That(drip.GetSemen(target), Is.EqualTo(130), "Overflow never costs the receiver their semen.");
            Assert.That(decals.GetDecalsInRange(grid, entMan.GetComponent<TransformComponent>(target).LocalPosition, 2f)
                .Any(d => d.Decal.Id.StartsWith("SemenPuddle")), Is.True,
                "An over-full womb must leave an overflow decal on the floor.");

            // The receive capacity is 1000u; anything past that is wasted.
            drip.AddSemen(target, 1000);
            Assert.That(drip.GetSemen(target), Is.EqualTo(1000), "A womb holds at most 1000u.");

            entMan.DeleteEntity(user);
            entMan.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Palmtree: "Clean yourself" wipes the stored semen and is only offered while there is any.
    /// </summary>
    [Test]
    public async Task CleanYourselfClearsTheDrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var interactions = server.System<InteractionPanelSystem>();
            var drip = server.System<DrippingCumSystem>();

            mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var clean = proto.Index<InteractionPrototype>("CleanYourself");

            // Nothing stored: the action is not offered.
            Assert.That(interactions.CanPerform(clean, mob, mob, false), Is.False);

            drip.AddSemen(mob, 30);

            // Make the drip overdue so a missed clear would visibly leave a droplet.
            var comp = entMan.GetComponent<DrippingCumComponent>(mob);
            comp.Ready = true;
            comp.NextDrip = TimeSpan.Zero;

            Assert.That(interactions.CanPerform(clean, mob, mob, false), Is.True);

            Assert.That(interactions.TryPerform(mob, mob, clean), Is.True);
            Assert.That(drip.GetSemen(mob), Is.EqualTo(0), "Cleaning should wipe the drip reservoir.");
            Assert.That(interactions.CanPerform(clean, mob, mob, false), Is.False,
                "The action disappears once there is nothing left to clean.");
        });

        // Give a ghost drip time to happen (the drip interval is one second).
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(mob);
            var decals = server.System<DecalSystem>()
                .GetDecalsInRange(xform.GridUid!.Value, xform.LocalPosition, 1f)
                .Count(d => d.Decal.Id.StartsWith("SemenDrip"));
            Assert.That(decals, Is.EqualTo(0), "Cleaning should stop the drip completely.");

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Palmtree: breast-owner and thigh-owner variants of the boobjob/thighjob interactions.
    /// </summary>
    [Test]
    public async Task BoobjobAndThighjobVariants()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var appearance = server.System<SharedHumanoidAppearanceSystem>();
            var interactions = server.System<InteractionPanelSystem>();

            var breasts = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var penis = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(1f, 0.5f)));

            var breastsSettings = new GenitalOrganSettings();
            breastsSettings.Set(GenitalType.Breasts, new GenitalOrganData { Prototype = "BreastsCoyote", Size = 2 });

            var penisSettings = new GenitalOrganSettings();
            penisSettings.Set(GenitalType.Penis, new GenitalOrganData { Prototype = "PenisHuman", Size = 2 });

            appearance.LoadProfile(breasts,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(breastsSettings));
            appearance.LoadProfile(penis,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(penisSettings));

            var giveBoobjob = proto.Index<InteractionPrototype>("GiveBoobjob");
            var thighjob = proto.Index<InteractionPrototype>("Thighjob");
            var giveThighjob = proto.Index<InteractionPrototype>("GiveThighjob");

            // The breast owner can start a boobjob on a penis owner, but not the reverse.
            Assert.That(interactions.CanPerform(giveBoobjob, breasts, penis, false), Is.True);
            Assert.That(interactions.CanPerform(giveBoobjob, penis, breasts, false), Is.False);

            // Thighjobs work in both directions.
            Assert.That(interactions.CanPerform(thighjob, penis, breasts, false), Is.True);
            Assert.That(interactions.CanPerform(giveThighjob, breasts, penis, false), Is.True);
            Assert.That(interactions.CanPerform(thighjob, breasts, penis, false), Is.False,
                "Fucking someone's thighs needs a penis.");

            entMan.DeleteEntity(breasts);
            entMan.DeleteEntity(penis);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Nudges the mob around inside its tile every tick, which is what arms the drip.
    /// </summary>
    private static async Task PokeMovement(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid mob, int ticks)
    {
        var entMan = server.ResolveDependency<IEntityManager>();
        var transform = server.System<SharedTransformSystem>();
        var start = entMan.GetComponent<TransformComponent>(mob).LocalPosition;

        for (var i = 0; i < ticks; i++)
        {
            var offset = i % 2 == 0 ? 0.1f : -0.1f;
            await server.WaitPost(() => transform.SetLocalPosition(mob, new Vector2(start.X + offset, start.Y)));
            await server.WaitRunTicks(1);
        }
    }

    private static int CountDripsOnTile(DecalSystem decals, EntityUid grid, Vector2 position)
    {
        return decals.GetDecalsInRange(grid, position, 1f)
            .Count(d => d.Decal.Id.StartsWith("SemenDrip") &&
                        MathF.Floor(d.Decal.Coordinates.X + 0.5f) == MathF.Floor(position.X) &&
                        MathF.Floor(d.Decal.Coordinates.Y + 0.5f) == MathF.Floor(position.Y));
    }

    [Test]
    public async Task LegacyGenitalMarkingsConvertToOrgans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var appearance = server.System<SharedHumanoidAppearanceSystem>();

            // Coyote-style export: old genital markings plus cross-species markings.
            const string yaml = """
forkId: ps14
version: 1
profile:
  species: Human
  appearance:
    markings:
    - markingId: PSGenitalBreasts7
      markingColor: ['#FFFFFFFF', '#FFFFFFFF', '#FFFFFFFF', '#FFFFFFFF']
    - markingId: GenitalVaginaHuman
      markingColor: ['#FFFFFFFF']
    - markingId: GenitalButt1
      markingColor: ['#FFFFFFFF', '#FFFFFFFF']
    - markingId: Genital-Balls-Sheath2
      markingColor: ['#FFFFFFFF']
    - markingId: TailBats
      markingColor: ['#FFFFFFFF', '#FFFFFFFF', '#FFFFFFFF', '#FFFFFFFF']
    - markingId: VulpEar
      markingColor: ['#FFFFFFFF', '#FFFFFFFF']
""";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
            var profile = appearance.FromStream(stream, null!);

            // Genital markings become organs.
            Assert.That(profile.Genitals.Get(GenitalType.Breasts)?.Prototype, Is.EqualTo("BreastsSplurt"));
            Assert.That(profile.Genitals.Get(GenitalType.Breasts)?.Size, Is.EqualTo(8));
            Assert.That(profile.Genitals.Get(GenitalType.Vagina)?.Prototype, Is.EqualTo("VaginaHuman"));
            Assert.That(profile.Genitals.Get(GenitalType.Butt)?.Prototype, Is.EqualTo("ButtStandard"));
            // Balls render their aroused (*Alt) sprite as the standard one, so the base marking
            // still has to convert.
            Assert.That(profile.Genitals.Get(GenitalType.Balls)?.Prototype, Is.EqualTo("BallsSheath"));
            Assert.That(profile.Genitals.Get(GenitalType.Balls)?.Size, Is.EqualTo(2));

            // Non-genital markings (including cross-species tails/ears) survive validation.
            Assert.That(profile.Appearance.Markings.Any(m => m.MarkingId == "TailBats"), Is.True,
                "Tail markings should survive import with the raised Human marking limits.");
            Assert.That(profile.Appearance.Markings.Any(m => m.MarkingId == "VulpEar"), Is.True);

            // The old genital markings are gone from the appearance.
            Assert.That(profile.Appearance.Markings.Any(m =>
                m.MarkingId.StartsWith("Genital") || m.MarkingId.StartsWith("PSGenital")), Is.False);

            // Offset/scale/glow round-trip through the DB string (colors omitted).
            var dbSettings = new GenitalOrganSettings();
            dbSettings.Set(GenitalType.Penis, new GenitalOrganData
            {
                Prototype = "PenisHuman",
                Size = 3,
                Offset = new Vector2(0.25f, -0.5f),
                Scale = 1.5f,
                Glow = 0.5f,
                DetailGlow = 0.25f,
            });
            var parsed = GenitalOrganSettings.FromDbString(dbSettings.ToDbString());
            Assert.That(parsed.Get(GenitalType.Penis)?.Offset, Is.EqualTo(new Vector2(0.25f, -0.5f)));
            Assert.That(parsed.Get(GenitalType.Penis)?.Scale, Is.EqualTo(1.5f));
            Assert.That(parsed.Get(GenitalType.Penis)?.Glow, Is.EqualTo(0.5f));
            Assert.That(parsed.Get(GenitalType.Penis)?.DetailGlow, Is.EqualTo(0.25f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StalePendingProfileDoesNotWipeGenitals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            var appearance = server.System<SharedHumanoidAppearanceSystem>();
            var organSystem = server.System<GenitalOrganSystem>();

            // Create the mob without running ComponentInit/MapInit so the body is not ready and the
            // first (default, genital-less) profile event gets queued as pending.
            mob = entMan.CreateEntityUninitialized("MobHuman", testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(mob,
                new HumanoidProfileAppliedEvent(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human")));

            // Finish initializing the body, then apply the real profile with genitals.
            entMan.InitializeAndStartEntity(mob);

            var settings = new GenitalOrganSettings();
            settings.Set(GenitalType.Penis, new GenitalOrganData { Prototype = "PenisHuman", Size = 3 });
            appearance.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(settings));

            Assert.That(organSystem.TryGetOrgan(mob, GenitalType.Penis, out _, out _), Is.True,
                "The real profile should create the penis organ.");
        });

        // The stale pending (default) profile must not re-sync over the real one on a later tick.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var organSystem = server.System<GenitalOrganSystem>();
            Assert.That(organSystem.TryGetOrgan(mob, GenitalType.Penis, out _, out _), Is.True,
                "A stale pending profile must not wipe the configured genitals.");

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    private static void Serialize<T>(IRobustSerializer serializer, T value)
    {
        using var stream = new MemoryStream();
        serializer.Serialize(stream, value!);
        stream.Position = 0;
        var copy = serializer.Deserialize<T>(stream);
        Assert.That(copy, Is.Not.Null);
    }
}
