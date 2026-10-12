#nullable enable
using System;
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Doors.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Doors.Components;
using Content.Shared.FixedPoint;
using Content.Shared._PS.Synth;
using Content.Shared._PS.Synth.DeadStartupButton;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Medical.Healing;
using Content.Shared.Mobs.Systems;
using Content.Shared.Prying.Components;
using Content.Shared.Prying.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: synth physiology - sealed chassis (no pressure damage), rebootable when dead, and the
/// synthetic repair topicals actually accept the Synth damage container.
/// </summary>
[TestFixture]
public sealed class SynthTest
{
    [Test]
    public async Task SynthPhysiology()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        EntityUid synth = default;
        EntityUid wreck = default;
        EntityUid medic = default;

        await server.WaitPost(() =>
        {
            var damageable = entMan.System<DamageableSystem>();
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");
            var heat = protoMan.Index<DamageTypePrototype>("Heat");
            var doAfter = entMan.System<SharedDoAfterSystem>();

            medic = entMan.SpawnEntity("MobHuman", map.GridCoords);

            // A synth that died with modest overkill can be rebooted.
            synth = entMan.SpawnEntity("MobSynth", map.GridCoords);
            damageable.TryChangeDamage(synth, new DamageSpecifier(blunt, 800));

            // A wrecked chassis fails to reboot.
            wreck = entMan.SpawnEntity("MobSynth", map.GridCoords);
            damageable.TryChangeDamage(wreck, new DamageSpecifier(heat, 1000));

            foreach (var target in new[] { synth, wreck })
            {
                doAfter.TryStartDoAfter(new DoAfterArgs(entMan, medic, TimeSpan.FromSeconds(0.1),
                    new SharedDeadStartupButtonSystem.OnDoAfterButtonPressedEvent(), target, target: target));
            }
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var mobState = entMan.System<MobStateSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<SynthComponent>(synth), Is.True, "MobSynth should be a synth");
                Assert.That(entMan.HasComponent<PressureImmunityComponent>(synth), Is.True,
                    "synths should be immune to pressure damage in space");
                Assert.That(entMan.HasComponent<DeadStartupButtonComponent>(synth), Is.True,
                    "synths should have the reboot button");
                Assert.That(entMan.GetComponent<FlammableComponent>(synth).Damage.GetTotal(), Is.EqualTo(FixedPoint2.Zero),
                    "synth fire damage should be visual only (lava and liquid plasma must not hurt synths)");
                Assert.That(mobState.IsDead(synth), Is.False, "reboot should revive a synth with modest overkill");
                Assert.That(mobState.IsDead(wreck), Is.True, "a wrecked chassis should fail to reboot");
            });

            // The synthetic repair topicals accept the Synth damage container.
            foreach (var item in new[] { "RepairPatch", "RepairSpray", "RepairSprayPlus", "RepairAutoinjector" })
            {
                var uid = entMan.SpawnEntity(item, map.GridCoords);
                var healing = entMan.GetComponent<HealingComponent>(uid);
                Assert.That(healing.DamageContainers?.Any(x => x.ToString() == "Synth"), Is.True,
                    $"{item} should heal the Synth damage container");
                entMan.DeleteEntity(uid);
            }

            // Palmtree: synths are extremely radiation-sensitive and no longer take extra heat damage.
            var damageableComp = entMan.GetComponent<DamageableComponent>(synth);
            var modifierSet = protoMan.Index<DamageModifierSetPrototype>(damageableComp.DamageModifierSetId!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(modifierSet.Coefficients.TryGetValue("Radiation", out var radiation) && radiation > 1f, Is.True,
                    "synths should be extremely sensitive to radiation");
                Assert.That(modifierSet.Coefficients.ContainsKey("Heat"), Is.False,
                    "synths should not take extra heat damage");
            });

            entMan.DeleteEntity(synth);
            entMan.DeleteEntity(wreck);
            entMan.DeleteEntity(medic);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Palmtree: synths have a built-in jaws of life - they can force doors open bare-handed,
    /// including powered and bolted airlocks, while a normal human cannot.
    /// </summary>
    [Test]
    public async Task SynthCanForceDoorsOpen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid airlock = default;
        EntityUid synth = default;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            // The test map only has one tile; add two more so the pry users stand next to the
            // door (at tile centers) instead of inside it - a closed door opens for bumping mobs.
            var mapSys = entMan.System<SharedMapSystem>();
            var tileDef = server.ResolveDependency<ITileDefinitionManager>()["Plating"];
            mapSys.SetTile(map.Grid.Owner, map.Grid.Comp,
                new EntityCoordinates(map.Grid.Owner, 1f, 0f), new Tile(tileDef.TileId));
            mapSys.SetTile(map.Grid.Owner, map.Grid.Comp,
                new EntityCoordinates(map.Grid.Owner, 2f, 0f), new Tile(tileDef.TileId));

            synth = entMan.SpawnEntity("MobSynth", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f));

            // Powered airlock - only a PryPowered user may force it.
            airlock = entMan.SpawnEntity("Airlock", map.GridCoords);
            entMan.SpawnEntity("APCBasic", map.GridCoords);
        });

        await pair.RunTicksSync(5);

        var pry = entMan.System<PryingSystem>();
        var door = entMan.System<DoorSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<AirlockComponent>(airlock).Powered, Is.True,
                    "airlock should be powered for this test");
                Assert.That(entMan.HasComponent<PryingComponent>(synth), Is.True,
                    "synths should have built-in prying");
                Assert.That(entMan.HasComponent<PryingComponent>(human), Is.False,
                    "humans should not have built-in prying");
            });
        });

        // Control: a human cannot force a powered airlock open.
        await server.WaitPost(() => pry.TryPry(airlock, human, out _, human));
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(airlock).State, Is.EqualTo(DoorState.Closed),
                "a human should not be able to force a powered airlock open");
        });

        // The synth forces the powered airlock open.
        await server.WaitPost(() => pry.TryPry(airlock, synth, out _, synth));
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(airlock).State, Is.Not.EqualTo(DoorState.Closed),
                "a synth should force a powered airlock open");
        });

        // And a bolted one, thanks to Force.
        await server.WaitPost(() =>
        {
            door.SetState(airlock, DoorState.Closed);
            door.SetBoltsDown((airlock, entMan.GetComponent<DoorBoltComponent>(airlock)), true);
            pry.TryPry(airlock, synth, out _, synth);
        });
        await pair.RunTicksSync(90); // let it finish opening
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(airlock).State, Is.EqualTo(DoorState.Open),
                "a synth should force a bolted airlock open");
        });

        // And it can force the bolted door closed again.
        await server.WaitPost(() => pry.TryPry(airlock, synth, out _, synth));
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(airlock).State, Is.EqualTo(DoorState.Closed),
                "a synth should force a bolted airlock closed");

            entMan.DeleteEntity(airlock);
            entMan.DeleteEntity(synth);
            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }
}
