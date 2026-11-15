// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server._Funkystation.Atmos.Components;
using Content.Server._Funkystation.Atmos.Portable;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     The electrolyzer and portable pipe scrubber both have an APC receiver, so neither may do
///     work while its APC is unpowered.
/// </summary>
[TestFixture]
public sealed class AtmosMachinesPowerTest
{
    [Test]
    public async Task ElectrolyzerNeedsApcPower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid electrolyzer = default;
        ElectrolyzerComponent electrolyzerComp = default!;

        await server.WaitAssertion(() =>
        {
            electrolyzer = entMan.SpawnEntity("Electrolyzer", testMap.GridCoords);
            electrolyzerComp = entMan.GetComponent<ElectrolyzerComponent>(electrolyzer);
            electrolyzerComp.IsPowered = true;
            electrolyzerComp.CurrentFuel = 0f;
        });

        await server.WaitAssertion(() =>
        {
            entMan.GetComponent<ApcPowerReceiverComponent>(electrolyzer).Powered = false;
            Raise(entMan, electrolyzer);

            Assert.That(electrolyzerComp.IsPowered, Is.True,
                "an unpowered electrolyzer ran its no-fuel shutdown");
        });

        await server.WaitAssertion(() =>
        {
            entMan.GetComponent<ApcPowerReceiverComponent>(electrolyzer).Powered = true;
            Raise(entMan, electrolyzer);

            Assert.That(electrolyzerComp.IsPowered, Is.False,
                "a powered electrolyzer with no fuel did not shut down");
        });

        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task CrystallizerNeedsApcPower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid crystallizer = default;
        CrystallizerComponent crystallizerComp = default!;

        await server.WaitAssertion(() =>
        {
            crystallizer = entMan.SpawnEntity("Crystallizer", testMap.GridCoords);
            crystallizerComp = entMan.GetComponent<CrystallizerComponent>(crystallizer);
            crystallizerComp.SelectedRecipeId = "metalHydrogenRecipe";
            crystallizerComp.TotalRecipeMoles = 350f;
            crystallizerComp.CrystallizerGasMixture.SetMoles(Gas.BZ, 50f);
            crystallizerComp.CrystallizerGasMixture.SetMoles(Gas.Hydrogen, 300f);
            crystallizerComp.CrystallizerGasMixture.Temperature = 60000f;
        });

        await server.WaitAssertion(() =>
        {
            entMan.GetComponent<ApcPowerReceiverComponent>(crystallizer).Powered = false;
            Raise(entMan, crystallizer);

            Assert.That(crystallizerComp.ProgressBar, Is.Zero,
                "an unpowered crystallizer advanced its recipe");
        });

        await server.WaitAssertion(() =>
        {
            entMan.GetComponent<ApcPowerReceiverComponent>(crystallizer).Powered = true;
            Raise(entMan, crystallizer);

            Assert.That(crystallizerComp.ProgressBar, Is.GreaterThan(0f),
                "a powered crystallizer with a valid mix did not advance its recipe");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PipeScrubberNeedsApcPower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var nodes = server.System<NodeContainerSystem>();

        EntityUid scrubber = default;
        PortablePipeNode port = default!;

        await server.WaitAssertion(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var plating = tiles["Plating"];
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    server.System<SharedMapSystem>().SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            scrubber = entMan.SpawnEntity("PipeScrubber", testMap.GridCoords);
            server.System<SharedTransformSystem>().AnchorEntity(scrubber);

            var comp = entMan.GetComponent<PipeScrubberComponent>(scrubber);
            comp.Enabled = true;

            Assert.That(nodes.TryGetNode(scrubber, comp.PortName, out port), Is.True,
                "the pipe scrubber has no portable pipe node");
            port.ConnectionsEnabled = true;
        });

        await server.WaitAssertion(() =>
        {
            entMan.GetComponent<ApcPowerReceiverComponent>(scrubber).Powered = false;
            port.ConnectionsEnabled = true;

            Raise(entMan, scrubber);

            Assert.That(port.ConnectionsEnabled, Is.False,
                "an unpowered scrubber stayed connected to its pipe net");
        });

        await server.WaitAssertion(() =>
        {
            entMan.GetComponent<ApcPowerReceiverComponent>(scrubber).Powered = true;

            Raise(entMan, scrubber);

            Assert.That(port.ConnectionsEnabled, Is.True,
                "a powered scrubber did not reconnect to its pipe net");
        });

        await pair.CleanReturnAsync();
    }

    private static void Raise(IEntityManager entMan, EntityUid uid)
    {
        var ev = new AtmosDeviceUpdateEvent(1f, null, null);
        entMan.EventBus.RaiseLocalEvent(uid, ref ev);
    }
}
