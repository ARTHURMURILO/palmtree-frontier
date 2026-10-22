using System.Collections.Generic;
using Content.Server.Light.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared._PS.Light.Components;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: check the dungeon light variants wire their effects up at runtime.
/// </summary>
[TestFixture]
public sealed class LightEffectsTest
{
    [Test]
    public async Task FlickeringFixtureBlinks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid fixture = default;

        await server.WaitAssertion(() =>
        {
            fixture = entMan.SpawnEntity("PoweredlightFlickering", testMap.GridCoords);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var powered = entMan.GetComponent<PoweredLightComponent>(fixture);
            var bulb = powered.LightBulbContainer.ContainedEntity;

            Assert.That(bulb, Is.Not.Null, "The fixture should spawn its flickering bulb.");
            Assert.That(entMan.HasComponent<FlickeringLightComponent>(bulb!.Value), Is.True);
            Assert.That(powered.IsBlinking, Is.True, "The flickering bulb should enable the fixture's blinking visuals.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AlwaysPoweredRaveFixtureIsLit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid fixture = default;

        await server.WaitAssertion(() =>
        {
            fixture = entMan.SpawnEntity("AlwaysPoweredlightRave", testMap.GridCoords);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var powered = entMan.GetComponent<PoweredLightComponent>(fixture);
            var bulb = powered.LightBulbContainer.ContainedEntity;
            var receiver = entMan.GetComponent<ApcPowerReceiverComponent>(fixture);
            var lights = server.System<SharedPointLightSystem>();

            Assert.That(receiver.NeedsPower, Is.False);
            Assert.That(receiver.Powered, Is.True, "The always-powered variant should work without an APC.");
            Assert.That(lights.TryGetLight(fixture, out var light), Is.True);
            Assert.That(light!.Enabled, Is.True, "The fixture should be lit.");

            Assert.That(entMan.HasComponent<RaveLightComponent>(fixture), Is.True);
            Assert.That(bulb, Is.Not.Null, "The fixture should still spawn a replaceable rave bulb.");
            Assert.That(entMan.HasComponent<RaveLightComponent>(bulb!.Value), Is.True);
            Assert.That(powered.IsBlinking, Is.False, "Rave lights should not use the flicker blinking visuals.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FlickerSystemDoesNotCancelOtherBlinking()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid fixture = default;

        await server.WaitAssertion(() =>
        {
            fixture = entMan.SpawnEntity("Poweredlight", testMap.GridCoords);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var powered = entMan.GetComponent<PoweredLightComponent>(fixture);
            server.System<PoweredLightSystem>().ToggleBlinkingLight(fixture, powered, true);
        });

        await server.WaitRunTicks(4);

        await server.WaitAssertion(() =>
        {
            var powered = entMan.GetComponent<PoweredLightComponent>(fixture);
            Assert.That(powered.IsBlinking, Is.True,
                "Blinking started by another system (e.g. the variation pass) must not be cancelled.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScrewdriverCyclesRavePeriod()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid fixture = default;
        EntityUid screwdriver = default;

        await server.WaitAssertion(() =>
        {
            fixture = entMan.SpawnEntity("PoweredlightRave", testMap.GridCoords);
            screwdriver = entMan.SpawnEntity("Screwdriver", testMap.GridCoords);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var rave = entMan.GetComponent<RaveLightComponent>(fixture);
            var xform = entMan.GetComponent<TransformComponent>(fixture);

            Assert.That(rave.Period, Is.EqualTo(1f));
            Assert.That(CyclePeriod(entMan, fixture, screwdriver, xform.Coordinates), Is.EqualTo(2f));
            Assert.That(CyclePeriod(entMan, fixture, screwdriver, xform.Coordinates), Is.EqualTo(0.5f));
            Assert.That(CyclePeriod(entMan, fixture, screwdriver, xform.Coordinates), Is.EqualTo(1f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RaveFixturePulsesOnClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var testMap = await pair.CreateTestMap();
        var serverEntMan = server.ResolveDependency<IEntityManager>();

        EntityUid fixture = default;
        NetEntity netFixture = default;

        await server.WaitAssertion(() =>
        {
            fixture = serverEntMan.SpawnEntity("PoweredlightRave", testMap.GridCoords);
            netFixture = serverEntMan.GetNetEntity(fixture);
        });

        await pair.RunTicksSync(5);

        var energies = new List<float>();
        for (var i = 0; i < 12; i++)
        {
            await pair.RunTicksSync(5);
            await client.WaitAssertion(() =>
            {
                var clientEntMan = client.ResolveDependency<IEntityManager>();
                var clientEnt = clientEntMan.GetEntity(netFixture);
                var light = clientEntMan.GetComponent<PointLightComponent>(clientEnt);
                energies.Add(light.Energy);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(energies, Has.Some.LessThan(0.4f), "The light should dim below half brightness.");
            Assert.That(energies, Has.Some.GreaterThan(0.6f), "The light should return to near full brightness.");
            Assert.That(energies, Has.All.InRange(0f, 0.81f));
        });

        await pair.CleanReturnAsync();
    }

    private static float CyclePeriod(
        IEntityManager entMan,
        EntityUid fixture,
        EntityUid screwdriver,
        EntityCoordinates coordinates)
    {
        var args = new InteractUsingEvent(fixture, screwdriver, fixture, coordinates);
        entMan.EventBus.RaiseLocalEvent(fixture, args);
        return entMan.GetComponent<RaveLightComponent>(fixture).Period;
    }
}
