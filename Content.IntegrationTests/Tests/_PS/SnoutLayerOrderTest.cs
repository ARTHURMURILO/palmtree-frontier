using System.Linq;
using Content.Client._PS.Humanoid;
using Content.Client.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the per-character "Snout above hair" option controls whether snout markings are lifted
/// above hair in east/west views.
/// </summary>
[TestFixture]
public sealed class SnoutLayerOrderTest
{
    [Test]
    public async Task ToggleMovesSnoutAboveHair()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false,
        });

        var server = pair.Server;
        var client = pair.Client;

        var serverPlayers = server.ResolveDependency<IPlayerManager>();

        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            var attached = serverPlayers.Sessions.Single().AttachedEntity;
            Assert.That(attached, Is.Not.Null);
            mob = attached.Value;

            var appearance = HumanoidCharacterAppearance.DefaultWithSpecies("Human");
            appearance.HairStyleId = "HumanHairBob";
            appearance.Markings.Add(new Marking("SnoutCanidAlt2", 1));

            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithCharacterAppearance(appearance);

            Assert.That(profile.SnoutAboveHair, Is.False,
                "the legacy snout-under-hair behavior must be the default");

            server.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, profile);
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var attached = client.Session.AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var humanoid = clientEntMan.GetComponent<HumanoidAppearanceComponent>(attached.Value);
            var appearance = client.System<HumanoidAppearanceSystem>();
            var direction = clientEntMan.EnsureComponent<HumanoidLayerDirectionComponent>(attached.Value);
            direction.Direction = RsiDirection.East;

            var sprite = clientEntMan.GetComponent<SpriteComponent>(attached.Value);
            var spriteSystem = client.System<SpriteSystem>();

            humanoid.SnoutAboveHair = false;
            appearance.RefreshSprite(attached.Value);

            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "SnoutCanidAlt2-snout_canid_alt2", out var underSnoutLayer, false), Is.True);
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "HumanHairBob-bob", out var underHairLayer, false), Is.True);
            Assert.That(underSnoutLayer, Is.LessThan(underHairLayer),
                "snouts must stay under hair when the option is disabled");

            humanoid.SnoutAboveHair = true;
            appearance.RefreshSprite(attached.Value);

            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "SnoutCanidAlt2-snout_canid_alt2", out var overSnoutLayer, false), Is.True);
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "HumanHairBob-bob", out var overHairLayer, false), Is.True);
            Assert.That(overSnoutLayer, Is.GreaterThan(overHairLayer),
                "snouts must render above hair when the option is enabled");

            // Diagonal side views must behave like east/west, not fall back to the south frame.
            clientEntMan.RemoveComponent<HumanoidLayerDirectionComponent>(attached.Value);
            client.System<SharedTransformSystem>().SetLocalRotation(attached.Value, Direction.SouthWest.ToAngle());
            appearance.RefreshSprite(attached.Value);

            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "SnoutCanidAlt2-snout_canid_alt2", out var diagonalSnoutLayer, false), Is.True);
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "HumanHairBob-bob", out var diagonalHairLayer, false), Is.True);
            Assert.That(diagonalSnoutLayer, Is.GreaterThan(diagonalHairLayer),
                "diagonal side views must also render snouts above hair");
        });

        await pair.CleanReturnAsync();
    }
}
