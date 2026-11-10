using System.Linq;
using Content.Shared._PS.Interactions;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: interaction panel layer preference and the client draw-depth watchdog.
/// </summary>
[TestFixture]
public sealed class CharacterLayerTest
{
    [Test]
    public async Task LayerPreferenceAppliesAndRepairsStaleDepth()
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

            var panel = serverEntMan.GetComponent<InteractionPanelComponent>(mob);
            panel.LayerMode = CharacterLayerMode.Above;
            serverEntMan.Dirty(mob, panel);
        });

        await pair.RunTicksSync(35);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var attached = client.Session.AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(attached.Value);
            Assert.That(sprite.DrawDepth, Is.EqualTo((int) DrawDepth.OverMobs),
                "Above must place the character over the default mob layer");
        });

        // Simulate a stale draw depth from another visual state. The watchdog should repair it.
        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var attached = client.Session.AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(attached.Value);
            client.System<SpriteSystem>().SetDrawDepth((attached.Value, sprite), (int) DrawDepth.Overlays);
        });

        await pair.RunTicksSync(35);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var attached = client.Session.AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(attached.Value);
            Assert.That(sprite.DrawDepth, Is.EqualTo((int) DrawDepth.OverMobs),
                "the layer watchdog must restore a stale draw depth");
        });

        await server.WaitAssertion(() =>
        {
            var panel = serverEntMan.GetComponent<InteractionPanelComponent>(mob);
            panel.LayerMode = CharacterLayerMode.Below;
            serverEntMan.Dirty(mob, panel);
        });

        await pair.RunTicksSync(35);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var attached = client.Session.AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(attached.Value);
            Assert.That(sprite.DrawDepth, Is.EqualTo((int) DrawDepth.BelowMobs),
                "Below must place the character under the default mob layer");
        });

        await server.WaitAssertion(() =>
        {
            var panel = serverEntMan.GetComponent<InteractionPanelComponent>(mob);
            panel.LayerMode = CharacterLayerMode.Default;
            serverEntMan.Dirty(mob, panel);
        });

        await pair.RunTicksSync(35);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var attached = client.Session.AttachedEntity;
            Assert.That(attached, Is.Not.Null);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(attached.Value);
            Assert.That(sprite.DrawDepth, Is.EqualTo((int) DrawDepth.Mobs),
                "Default must restore the normal mob layer");
        });

        // The watchdog is not limited to interaction-panel mobs: every living mob is kept on its
        // own base draw depth. Mice use SmallMobs instead of Mobs.
        NetEntity mouseNet = default;
        await server.WaitAssertion(() =>
        {
            var coordinates = serverEntMan.GetComponent<TransformComponent>(mob).Coordinates;
            var mouse = serverEntMan.SpawnEntity("MobMouse", coordinates);
            mouseNet = serverEntMan.GetNetEntity(mouse);
        });

        await pair.RunTicksSync(35);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            Assert.That(clientEntMan.TryGetEntity(mouseNet, out var mouse), Is.True);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(mouse.Value);
            Assert.That(sprite.DrawDepth, Is.EqualTo((int) DrawDepth.SmallMobs),
                "non-humanoid mobs keep their own base draw depth");
        });

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            Assert.That(clientEntMan.TryGetEntity(mouseNet, out var mouse), Is.True);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(mouse.Value);
            client.System<SpriteSystem>().SetDrawDepth((mouse.Value, sprite), (int) DrawDepth.Overlays);
        });

        await pair.RunTicksSync(35);

        await client.WaitAssertion(() =>
        {
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            Assert.That(clientEntMan.TryGetEntity(mouseNet, out var mouse), Is.True);

            var sprite = clientEntMan.GetComponent<SpriteComponent>(mouse.Value);
            Assert.That(sprite.DrawDepth, Is.EqualTo((int) DrawDepth.SmallMobs),
                "the watchdog must repair stale draw depth for non-humanoid living mobs too");
        });

        await pair.CleanReturnAsync();
    }
}
