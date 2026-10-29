using System.Numerics;
using Content.Server._PS.ShipCombat;
using Content.Server.Power.Components;
using Content.Shared._PS.ShipCombat;
using Content.Shared.Audio;
using Content.Shared.Light.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: condition sirens and battle-station lights follow the grid's condition automatically.
/// </summary>
[TestFixture]
public sealed class ShipConditionDevicesTest
{
    [Test]
    public async Task SirenAndBattleLightFollowTheCondition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();

        EntityUid redSiren = default;
        EntityUid yellowSiren = default;
        EntityUid light = default;

        await server.WaitPost(() =>
        {
            var gridEnt = mapMan.CreateGridEntity(testMap.MapId);
            var grid = gridEnt.Owner;
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(0, 0), new Tile(1));
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(1, 0), new Tile(1));
            mapSystem.SetTile(grid, gridEnt.Comp, new Vector2i(2, 0), new Tile(1));

            redSiren = entMan.SpawnEntity("ShipConditionSirenRedVoyager",
                new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            yellowSiren = entMan.SpawnEntity("ShipConditionSirenYellow",
                new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            light = entMan.SpawnEntity("PoweredlightShipCombat",
                new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));

            // The test grid has no APC, so treat the sirens as powered (their power gate is separate).
            entMan.RemoveComponent<ApcPowerReceiverComponent>(redSiren);
            entMan.RemoveComponent<ApcPowerReceiverComponent>(yellowSiren);

            server.System<ShipCombatSystem>().TryEnsureShipCombat(grid, out _);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryGetShipCombat(redSiren, out var state), Is.True);

            // GREEN: both sirens are silent and the battle light is on.
            Assert.That(GetAmbient(entMan, redSiren).Enabled, Is.False, "The red siren must be off at GREEN.");
            Assert.That(GetAmbient(entMan, yellowSiren).Enabled, Is.False, "The yellow siren must be off at GREEN.");
            Assert.That(GetLight(entMan, light).On, Is.True, "The battle light must be on at GREEN.");

            // YELLOW: only the yellow siren sounds, the battle light stays on.
            combat.SetCondition(state, ShipCondition.Yellow);
            Assert.That(GetAmbient(entMan, redSiren).Enabled, Is.False, "The red siren must be off at YELLOW.");
            Assert.That(GetAmbient(entMan, yellowSiren).Enabled, Is.True, "The yellow siren must sound at YELLOW.");
            Assert.That(GetLight(entMan, light).On, Is.True, "The battle light must stay on at YELLOW.");

            // RED: the red siren takes over and the battle light cuts out.
            combat.SetCondition(state, ShipCondition.Red);
            Assert.That(GetAmbient(entMan, redSiren).Enabled, Is.True, "The red siren must sound at RED.");
            Assert.That(GetAmbient(entMan, yellowSiren).Enabled, Is.False, "The yellow siren must stop at RED.");
            Assert.That(GetLight(entMan, light).On, Is.False, "The battle light must be off at RED.");
        });

        await pair.CleanReturnAsync();
    }

    private static AmbientSoundComponent GetAmbient(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<AmbientSoundComponent>(uid);
    }

    private static PoweredLightComponent GetLight(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<PoweredLightComponent>(uid);
    }
}
