using System.Reflection;
using Content.Server.StationEvents;
using Content.Server.StationEvents.Components;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the NFSD vault bluespace events only run while at least one NFSD job is filled.
/// <see cref="EventManagerSystem.CanRun"/> is private, so it is invoked by reflection (the same
/// approach other scratch tests use when the subscription lock blocks a real handler).
/// </summary>
[TestFixture]
public sealed class NfsdVaultEventGatingTest
{
    [Test]
    public async Task VaultEventsNeedAnNfsdCharacter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var events = server.System<EventManagerSystem>();
            var canRun = typeof(EventManagerSystem)
                .GetMethod("CanRun", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(canRun, Is.Not.Null, "EventManagerSystem.CanRun should exist.");

            // No character holds an NFSD job in a fresh test round.
            const int playerCount = 10;
            var currentTime = TimeSpan.FromMinutes(200); // Past earliestStart.

            foreach (var id in new[] { "BluespaceVaultError", "BluespaceVaultSmallError" })
            {
                var eventProto = proto.Index<EntityPrototype>(id);
                var stationEvent = (StationEventComponent) eventProto.Components["StationEvent"].Component;
                Assert.That(stationEvent.RequiredJobs, Is.Not.Empty,
                    $"{id} should be gated on NFSD jobs.");

                var result = canRun!.Invoke(events, new object[] { eventProto, stationEvent, playerCount, currentTime });
                Assert.That(result, Is.EqualTo(false),
                    $"{id} must not run without an active NFSD character.");
            }

            // Control: the same call passes for the same prototype when no jobs are required, so the
            // failures above come from the job gate rather than some other condition.
            var controlProto = proto.Index<EntityPrototype>("BluespaceVaultError");
            var control = new StationEventComponent();
            var controlResult = canRun!.Invoke(events, new object[] { controlProto, control, playerCount, currentTime });
            Assert.That(controlResult, Is.EqualTo(true),
                "The vault event would otherwise be able to run.");
        });

        await pair.CleanReturnAsync();
    }
}
