using Content.Server._PS.Audio;
using Content.Server.Light.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Shared.Light.Components;
using NUnit.Framework;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the cyberpunk siren strobes start off and keep a looping long-range stream while lit.
/// </summary>
[TestFixture]
public sealed class LongRangeSirenTest
{
    [TestCase("PoweredStrobeLightNCPD")]
    [TestCase("PoweredStrobeLightMaxTac")]
    public async Task SirenStartsOffAndPlaysWhenLit(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var receivers = server.System<PowerReceiverSystem>();
        var lights = server.System<PoweredLightSystem>();

        EntityUid siren = default;

        await server.WaitAssertion(() =>
        {
            siren = entMan.SpawnEntity(prototype, testMap.GridCoords);
            receivers.SetNeedsPower(siren, false);
        });

        await server.WaitRunTicks(70);

        await server.WaitAssertion(() =>
        {
            var light = entMan.GetComponent<PoweredLightComponent>(siren);
            var sirenComp = entMan.GetComponent<LongRangeSirenComponent>(siren);

            Assert.That(light.CurrentLit, Is.False, "The siren should start switched off.");
            Assert.That(sirenComp.Stream, Is.Null, "An unlit siren should not have an audio stream.");
        });

        await server.WaitAssertion(() =>
        {
            var light = entMan.GetComponent<PoweredLightComponent>(siren);
            lights.SetState(siren, true, light);
        });

        await server.WaitRunTicks(70);

        EntityUid? stream = null;

        await server.WaitAssertion(() =>
        {
            var sirenComp = entMan.GetComponent<LongRangeSirenComponent>(siren);
            stream = sirenComp.Stream;

            Assert.That(stream, Is.Not.Null, "The lit siren should start its long-range stream.");
            Assert.That(entMan.TryGetComponent<AudioComponent>(stream, out var audio), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(audio!.Params.Loop, Is.True);
                Assert.That(audio.Params.MaxDistance, Is.EqualTo(sirenComp.MaxDistance));
                Assert.That(audio.Params.ReferenceDistance, Is.EqualTo(sirenComp.ReferenceDistance));
                Assert.That(audio.Flags.HasFlag(AudioFlags.NoOcclusion), Is.True);
            });
        });

        await server.WaitAssertion(() =>
        {
            var light = entMan.GetComponent<PoweredLightComponent>(siren);
            lights.SetState(siren, false, light);
        });

        await server.WaitRunTicks(70);

        await server.WaitAssertion(() =>
        {
            var sirenComp = entMan.GetComponent<LongRangeSirenComponent>(siren);
            Assert.That(sirenComp.Stream, Is.Null, "Switching the siren off should stop its stream.");
            Assert.That(entMan.EntityExists(stream), Is.False, "The stream entity should be cleaned up.");
        });

        await pair.CleanReturnAsync();
    }
}
