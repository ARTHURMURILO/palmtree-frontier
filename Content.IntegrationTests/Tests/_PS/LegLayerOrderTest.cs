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

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: in side views both leg base sprites are drawn on the same pixels, so a marking on the
/// leg nearest the camera must render above the far leg. Facing east that is the right leg; facing
/// west it is the left leg.
/// </summary>
[TestFixture]
public sealed class LegLayerOrderTest
{
    [Test]
    public async Task RightLegMarkingRendersOverLeftLegFacingEast()
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

            var appearance = HumanoidCharacterAppearance.DefaultWithSpecies("Arachnid");
            appearance.Markings.Add(new Marking("ArachnidRLegStripes", 1));
            appearance.Markings.Add(new Marking("ArachnidLLegStripes", 1));

            server.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob,
                HumanoidCharacterProfile.DefaultWithSpecies("Arachnid").WithCharacterAppearance(appearance));
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var attached = client.Session.AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var appearance = client.System<HumanoidAppearanceSystem>();
            var direction = clientEntMan.EnsureComponent<HumanoidLayerDirectionComponent>(attached.Value);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(attached.Value);
            var spriteSystem = client.System<SpriteSystem>();

            direction.Direction = RsiDirection.East;
            appearance.RefreshSprite(attached.Value);

            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "ArachnidRLegStripes-stripes_right", out var eastRightStripe, false), Is.True,
                "the right-leg marking must render in the east view");
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                HumanoidVisualLayers.RLeg, out var eastRightLeg, false), Is.True);
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                HumanoidVisualLayers.LLeg, out var eastLeftLeg, false), Is.True);
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                HumanoidVisualLayers.GenitalLow, out var eastGenitalLow, false), Is.True);

            Assert.That(eastRightStripe, Is.GreaterThan(eastRightLeg),
                "a leg marking must render above its own leg");
            Assert.That(eastRightStripe, Is.GreaterThan(eastLeftLeg),
                "the near right leg's marking must render above the far left leg");
            Assert.That(eastRightStripe, Is.LessThan(eastGenitalLow),
                "lifting a leg marking must not push it above the genital sublayer");

            direction.Direction = RsiDirection.West;
            appearance.RefreshSprite(attached.Value);

            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                "ArachnidLLegStripes-stripes_left", out var westLeftStripe, false), Is.True,
                "the left-leg marking must render in the west view");
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                HumanoidVisualLayers.RLeg, out var westRightLeg, false), Is.True);
            Assert.That(spriteSystem.LayerMapTryGet((attached.Value, sprite),
                HumanoidVisualLayers.LLeg, out var westLeftLeg, false), Is.True);

            Assert.That(westLeftStripe, Is.GreaterThan(westLeftLeg),
                "a leg marking must render above its own leg");
            Assert.That(westLeftStripe, Is.GreaterThan(westRightLeg),
                "the near left leg's marking must render above the far right leg");

            clientEntMan.RemoveComponent<HumanoidLayerDirectionComponent>(attached.Value);
        });

        await server.WaitAssertion(() =>
        {
            var attached = serverPlayers.Sessions.Single().AttachedEntity;
            if (attached != null)
            {
                server.System<SharedHumanoidAppearanceSystem>().LoadProfile(attached.Value,
                    HumanoidCharacterProfile.DefaultWithSpecies("Human"));
            }
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }
}
