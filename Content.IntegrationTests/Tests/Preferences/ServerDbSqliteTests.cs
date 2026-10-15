using System.Collections.Generic;
using System.Linq;
using Content.Server.Database;
using Content.Shared._PS.Consent; // Palmtree
using Content.Shared._PS.Interactions; // Palmtree
using Content.Shared._PS.Organs; // Palmtree
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Preferences
{
    [TestFixture]
    public sealed class ServerDbSqliteTests
    {
        [TestPrototypes]
        private const string Prototypes = @"
- type: dataset
  id: sqlite_test_names_first_male
  values:
  - Aaden

- type: dataset
  id: sqlite_test_names_first_female
  values:
  - Aaliyah

- type: dataset
  id: sqlite_test_names_last
  values:
  - Ackerley";

        private static HumanoidCharacterProfile CharlieCharlieson()
        {
            var profile = new HumanoidCharacterProfile() // Frontier - added HumanoidCharacterProfile
            {
                Name = "Charlie Charlieson",
                FlavorText = "The biggest boy around.",
                Species = "Human",
                VoiceBark = "Alto", // Palmtree: voice bark round-trip
                Age = 21,
                Height = 1.75f, // Palmtree: height/width round-trip
                Width = 0.8f, // Palmtree
                Appearance = new(
                    "Afro",
                    Color.Aqua,
                    "Shaved",
                    Color.Aquamarine,
                    Color.Azure,
                    Color.Beige,
                    new (),
                    HumanoidLegStyle.Digitigrade) // Palmtree/Coyote: leg style round-trip
            };

            // Palmtree: genital organ + semen volume round-trip.
            profile.Genitals.Set(GenitalType.Penis, new GenitalOrganData
            {
                Prototype = "PenisHuman",
                Size = 3,
                Visibility = GenitalVisibility.NeverHidden,
                Offset = new System.Numerics.Vector2(0.25f, -0.5f),
            });
            profile.Genitals.Set(GenitalType.Vagina, new GenitalOrganData
            {
                Prototype = "VaginaHuman",
                Size = 1,
            });
            profile.Genitals.SemenVolume = 42;

            return profile.WithBankBalance(27000); // Frontier - accessor issue
        }

        private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
        {
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
            var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            builder.UseSqlite(conn);
            return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
        }

        [Test]
        public async Task TestUserDoesNotExist()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            // Database should be empty so a new GUID should do it.
            Assert.That(await db.GetPlayerPreferencesAsync(NewUserId()), Is.Null);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestInitPrefs()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            const int slot = 0;
            var originalProfile = CharlieCharlieson();
            await db.InitPrefsAsync(username, originalProfile);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(prefs.Characters.Single(p => p.Key == slot).Value.MemberwiseEquals(originalProfile));
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestNsfwConsentRoundTrip()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var username = NewUserId();

            // Fresh accounts have never answered the age gate.
            await db.InitPrefsAsync(username, new HumanoidCharacterProfile());
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(prefs.NsfwConsent, Is.EqualTo(NsfwConsentState.Unanswered));

            // Both answers round-trip through the nullable column.
            await db.SaveNsfwConsentAsync(username, NsfwConsentState.Allowed);
            prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(prefs.NsfwConsent, Is.EqualTo(NsfwConsentState.Allowed));

            await db.SaveNsfwConsentAsync(username, NsfwConsentState.Denied);
            prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(prefs.NsfwConsent, Is.EqualTo(NsfwConsentState.Denied));

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestDeleteCharacter()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            await db.InitPrefsAsync(username, new HumanoidCharacterProfile());
            await db.SaveCharacterSlotAsync(username, CharlieCharlieson(), 1);
            await db.SaveSelectedCharacterIndexAsync(username, 1);
            await db.SaveCharacterSlotAsync(username, null, 1);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(!prefs.Characters.Any(p => p.Key != 0));
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestNoPendingDatabaseChanges()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            Assert.That(async () => await db.HasPendingModelChanges(), Is.False,
                "The database has pending model changes. Add a new migration to apply them. See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations");
            await pair.CleanReturnAsync();
        }

        private static NetUserId NewUserId()
        {
            return new(Guid.NewGuid());
        }
    }
}
