using System.Linq;
using Content.Shared._PS.Interactions;
using Content.Shared._PS.Organs;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: regression tests for organ-driven genital rendering. The server is the only side that
/// may rebuild the <see cref="MarkingCategories.Genital"/> marking category; network clients render
/// the networked marking set as-is.
/// </summary>
[TestFixture]
public sealed class GenitalRenderTest
{
    /// <summary>
    /// Robust ejects contained entities from their containers when a network client detaches an
    /// entity for PVS, which fires <see cref="Content.Shared.Body.Events.OrganRemovedFromBodyEvent"/>
    /// on the client. The client used to rebuild the genital markings from local organ state at that
    /// point; with no organs found it stripped the category, and because the client's marking set is
    /// also its cached component state object the wipe stuck through PVS re-entry (genitals
    /// disappeared for observers who walked away and returned, or joined as a ghost).
    /// </summary>
    [Test]
    public async Task ClientDoesNotRebuildOrganRenderMarkings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false,
        });

        var server = pair.Server;
        var client = pair.Client;

        var serverEntMan = server.ResolveDependency<IEntityManager>();
        var serverPlayers = server.ResolveDependency<IPlayerManager>();

        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            var attached = serverPlayers.Sessions.Single().AttachedEntity;
            Assert.That(attached, Is.Not.Null);
            mob = attached.Value;

            var appearance = server.System<SharedHumanoidAppearanceSystem>();
            var organSystem = server.System<GenitalOrganSystem>();

            var settings = new GenitalOrganSettings();
            settings.Set(GenitalType.Penis, new GenitalOrganData
            {
                Prototype = "PenisHuman",
                Size = 3,
                Visibility = GenitalVisibility.NeverHidden,
            });
            settings.Set(GenitalType.Balls, new GenitalOrganData
            {
                Prototype = "BallsSingle",
                Size = 1,
                Visibility = GenitalVisibility.NeverHidden,
            });

            appearance.LoadProfile(mob,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithGenitals(settings));

            Assert.That(organSystem.TryGetOrgan(mob, GenitalType.Penis, out _, out _), Is.True);
            Assert.That(HasGenitalMarking(serverEntMan.GetComponent<HumanoidAppearanceComponent>(mob)),
                Is.True, "the server must render the organ marking");

            // Marking layers inserted at the same index render newest-first, so organs are added in
            // reverse priority order. The penis must therefore appear before the balls in the list
            // (and above them in the final sprite stack).
            Assert.That(serverEntMan.GetComponent<HumanoidAppearanceComponent>(mob).MarkingSet
                    .TryGetCategory(MarkingCategories.Genital, out var genitalMarkings), Is.True);
            var genitalMarkingList = genitalMarkings.ToList();
            var penisIndex = genitalMarkingList.FindIndex(m => m.MarkingId == "Genital-Penis-Human-3-0");
            var ballsIndex = genitalMarkingList.FindIndex(m => m.MarkingId == "Genital-Balls-Single1");
            Assert.That(penisIndex, Is.GreaterThanOrEqualTo(0), "the penis render marking must exist");
            Assert.That(ballsIndex, Is.GreaterThanOrEqualTo(0), "the balls render marking must exist");
            Assert.That(penisIndex, Is.LessThan(ballsIndex),
                "organs must be added back-to-front so the penis renders above the balls");
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var mob = client.Session.AttachedEntity;
            Assert.That(mob, Is.Not.Null);

            var humanoid = clientEntMan.GetComponent<HumanoidAppearanceComponent>(mob.Value);
            Assert.That(humanoid.MarkingSet.TryGetCategory(MarkingCategories.Genital, out var markings), Is.True,
                "the server's organ marking must replicate to the client");
            Assert.That(markings, Is.Not.Empty, "the server's organ marking must replicate to the client");

            // Snapshot the networked marking objects; the client must leave them alone.
            var before = markings.ToList();

            client.System<GenitalOrganSystem>().SyncRender(mob.Value);

            Assert.That(humanoid.MarkingSet.TryGetCategory(MarkingCategories.Genital, out var after), Is.True,
                "a network client must keep the server's genital render markings");
            Assert.That(after.Count, Is.EqualTo(before.Count),
                "a network client must not rebuild the genital render markings");
            for (var i = 0; i < before.Count; i++)
            {
                Assert.That(after[i], Is.SameAs(before[i]),
                    "the client must leave the networked marking objects untouched");
            }
        });

        // Strip the organs again before returning the pair: deleting organ-bearing mobs during
        // pool recycling trips a DEBUG-only client body assert.
        await server.WaitAssertion(() =>
        {
            var appearance = server.System<SharedHumanoidAppearanceSystem>();
            appearance.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human"));
        });

        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }

    private static bool HasGenitalMarking(HumanoidAppearanceComponent humanoid)
    {
        return humanoid.MarkingSet.TryGetCategory(MarkingCategories.Genital, out var markings)
               && markings.Count > 0;
    }
}
