#nullable enable
using Content.Server._PS.Circuitry.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryPowerTest
{
    [Test]
    public async Task FullyLoadedAssemblyDrawsLessThanMicroreactorRecharge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid assembly = default;

        await server.WaitPost(() =>
        {
            assembly = entMan.SpawnEntity("CircuitAssemblyFilled", map.GridCoords);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var component = entMan.GetComponent<CircuitAssemblyComponent>(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(component.Powered, Is.True);
                Assert.That(component.Cpu, Is.Not.Null);
                Assert.That(component.Radio, Is.Not.Null);
                Assert.That(component.Hands, Is.EqualTo(2));
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.Locomotion), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.Manipulator), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.Voice), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.Sensor), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.Radio), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.Reagent), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.BotanicalAnalyzer), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.ChemicalAnalyzer), Is.True);
                Assert.That(component.Capabilities.HasFlag(CircuitCapability.MedicalAnalyzer), Is.True);
                Assert.That(component.IdlePowerDraw, Is.LessThan(2f),
                    "fully loaded idle draw should stay well below the microreactor's 12/s recharge");
                Assert.That(component.ActionPowerCost, Is.LessThanOrEqualTo(0.1f));
                Assert.That(component.MovePowerPerMeter, Is.LessThanOrEqualTo(0.1f));
            });
        });

        await pair.CleanReturnAsync();
    }
}
