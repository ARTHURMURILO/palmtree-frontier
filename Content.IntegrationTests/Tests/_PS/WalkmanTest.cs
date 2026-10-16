using Content.Server.Instruments;
using Content.Shared.UserInterface;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the walkman family - the instrument UI survives being pocketed, both variants
/// carry the full General MIDI program list, and the super walkman plays like the super
/// synthesizer (percussion, program changes, no client MIDI rate cap).
/// </summary>
[TestFixture]
public sealed class WalkmanTest
{
    [Test]
    public async Task WalkmanVariants()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var walkman = entMan.SpawnEntity("WalkmanInstrument", testMap.GridCoords);
            var superWalkman = entMan.SpawnEntity("SuperWalkmanInstrument", testMap.GridCoords);

            foreach (var uid in new[] { walkman, superWalkman })
            {
                // Pocketable: the instrument UI - and the music - survives being put away.
                Assert.That(entMan.GetComponent<ActivatableUIComponent>(uid).InHandsOnly, Is.False,
                    "Walkman instruments keep their UI open when stored.");
                // Full General MIDI program list inherited from the synthesizer.
                Assert.That(entMan.GetComponent<SwappableInstrumentComponent>(uid).InstrumentList, Has.Count.EqualTo(128),
                    "Walkmans carry the full instrument list.");
            }

            // The stock walkman respects the client MIDI limits...
            Assert.That(entMan.GetComponent<InstrumentComponent>(walkman).RespectMidiLimits, Is.True,
                "The stock walkman should respect the MIDI limits.");

            // ...while the super walkman is the super synthesizer's instrument in walkman form.
            var super = entMan.GetComponent<InstrumentComponent>(superWalkman);
            Assert.That(super.RespectMidiLimits, Is.False, "The super walkman should ignore the MIDI rate limits.");
            Assert.That(super.AllowPercussion, Is.True, "The super walkman should allow percussion.");
            Assert.That(super.AllowProgramChange, Is.True, "The super walkman should allow MIDI program changes.");

            entMan.DeleteEntity(walkman);
            entMan.DeleteEntity(superWalkman);
        });

        await pair.CleanReturnAsync();
    }
}
