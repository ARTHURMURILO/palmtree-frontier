using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Construction.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS.Atmos;

/// <summary>
/// Palmtree: the vanilla freezer, heater and their hellfire versions react to their matter bins.
/// </summary>
/// <remarks>
/// The thermomachine's heat capacity is also its power draw, so a bin upgrade has to raise both and
/// leave the coefficient of performance alone. The hellfire machines override the stock heat
/// capacity in their prototype, so they get their own base value; without it installing parts would
/// downgrade them.
/// </remarks>
[TestFixture]
public sealed class ThermomachineUpgradeTest
{
    [Test]
    public async Task ThermomachineScalesWithItsMatterBins()
    {
        await using var pair = await PoolManager.GetServerClient();

        try
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var testMap = await pair.CreateTestMap();

            var coords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            EntityUid freezer = default;
            EntityUid hellfire = default;

            await server.WaitPost(() =>
            {
                freezer = entMan.SpawnEntity("GasThermoMachineFreezer", coords);
                hellfire = entMan.SpawnEntity("GasThermoMachineHellfireFreezer", coords);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var component = entMan.GetComponent<GasThermoMachineComponent>(freezer);

                Assert.That(component.HeatCapacity, Is.EqualTo(component.BaseHeatCapacity),
                    "a machine with stock parts should be on the stock heat capacity");

                Raise(entMan, freezer, component.MachinePartHeatCapacity, 4f);

                Assert.That(component.HeatCapacity, Is.EqualTo(component.BaseHeatCapacity * 4f).Within(1f),
                    "a tier four matter bin should quadruple the heat moved and the power draw");

                Raise(entMan, freezer, component.MachinePartTemperature, 4f);

                Assert.Multiple(() =>
                {
                    Assert.That(component.MinTemperature, Is.EqualTo(28.15f).Within(0.01f),
                        "tier four capacitors should let the freezer be set 45 K colder");
                    Assert.That(component.MaxTemperature, Is.EqualTo(1193.15f).Within(0.01f),
                        "tier four capacitors should let the freezer be set 600 K hotter");
                });

                var hellfireComponent = entMan.GetComponent<GasThermoMachineComponent>(hellfire);

                Assert.Multiple(() =>
                {
                    Assert.That(hellfireComponent.BaseHeatCapacity, Is.EqualTo(40_000f),
                        "the hellfire machine should scale from its own heat capacity, not the stock 5000");
                    Assert.That(hellfireComponent.HeatCapacity, Is.EqualTo(40_000f),
                        "a hellfire machine with stock parts should start where its prototype says");
                });

                Raise(entMan, hellfire, hellfireComponent.MachinePartHeatCapacity, 3f);

                Assert.That(hellfireComponent.HeatCapacity, Is.EqualTo(120_000f).Within(1f),
                    "a tier three matter bin should triple the hellfire machine's heat capacity");

                Raise(entMan, hellfire, hellfireComponent.MachinePartTemperature, 3f);

                Assert.Multiple(() =>
                {
                    Assert.That(hellfireComponent.MinTemperature, Is.EqualTo(Atmospherics.TCMB).Within(0.01f),
                        "the hellfire freezer's own base is cold enough that better capacitors hit the gas floor");
                    Assert.That(hellfireComponent.MaxTemperature, Is.EqualTo(993.15f).Within(0.01f),
                        "capacitors should widen the hellfire freezer's range from its own base");
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static void Raise(IEntityManager entMan, EntityUid uid, string part, float rating)
    {
        entMan.EventBus.RaiseLocalEvent(uid, new RefreshPartsEvent
        {
            PartRatings = new Dictionary<string, float>
            {
                [part] = rating,
            },
        }, true);
    }
}
