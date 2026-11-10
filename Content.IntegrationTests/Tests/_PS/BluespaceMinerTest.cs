using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._PS.Materials.Components;
using Content.Server.Power.Components;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the bluespace miner should mine basic ores and deposit them into a linked ore silo.
/// </summary>
[TestFixture]
public sealed class BluespaceMinerTest
{
    private static readonly HashSet<string> BasicOres = new()
    {
        "RawIron",
        "RawQuartz",
        "Coal",
        "RawSilver",
        "RawGold",
        "RawSalt",
    };

    [Test]
    public async Task MinesOresIntoSilo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        EntityUid miner = default;
        EntityUid silo = default;

        await server.WaitAssertion(() =>
        {
            var transform = server.System<SharedTransformSystem>();

            // Keep all three machines on the test map's single tile so the APC's extension-cable
            // field reaches both receivers without building a larger grid.
            var apc = entMan.SpawnEntity("APCBasic", testMap.GridCoords.Offset(new Vector2(0.2f, 0.2f)));
            Anchor(transform, entMan, apc);

            silo = entMan.SpawnEntity("MachineMaterialSilo", testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            Anchor(transform, entMan, silo);

            miner = entMan.SpawnEntity("PSBluespaceMiner", testMap.GridCoords.Offset(new Vector2(0.8f, 0.8f)));
            Anchor(transform, entMan, miner);

            var minerComp = entMan.GetComponent<BluespaceMinerComponent>(miner);
            minerComp.Enabled = false;
            minerComp.OreWeights.Clear();
            minerComp.OreWeights["RawIron"] = 1f;
            minerComp.AmountPerCycle = 100;
            minerComp.SecondsPerCycle = 0.05f;
            minerComp.NextMiningTime = TimeSpan.Zero;
        });

        // Let the APC establish its extension-cable links before linking the machines.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(miner).Powered, Is.True,
                "The miner should be powered by the test APC.");

            // Link the miner the same way the silo UI does, then start it.
            entMan.EventBus.RaiseLocalEvent(silo, new ToggleOreSiloClientMessage(entMan.GetNetEntity(miner)));
            var minerComp = entMan.GetComponent<BluespaceMinerComponent>(miner);
            minerComp.Enabled = true;
            minerComp.NextMiningTime = TimeSpan.Zero;
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var storage = entMan.GetComponent<MaterialStorageComponent>(silo).Storage;
            Assert.That(storage.TryGetValue("RawIron", out var amount), Is.True,
                "The silo did not receive any iron ore.");
            Assert.That(amount, Is.GreaterThan(0), "The silo received a non-positive amount of iron ore.");
            Assert.That(storage.Keys.Select(key => key.Id), Is.SubsetOf(BasicOres),
                "The miner produced an ore outside the basic ore list.");
        });

        await pair.CleanReturnAsync();
    }

    private static void Anchor(SharedTransformSystem transform, IEntityManager entMan, EntityUid uid)
    {
        if (!entMan.GetComponent<TransformComponent>(uid).Anchored)
            transform.AnchorEntity(uid);
    }
}
