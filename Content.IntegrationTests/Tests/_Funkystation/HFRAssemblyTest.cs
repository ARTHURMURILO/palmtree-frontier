// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server._Funkystation.Atmos.Components;
using Content.Server._Funkystation.Atmos.HFR.Systems;
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Funkystation;

/// <summary>
///     Guards the HFR multiblock pattern: the core must find the console and side parts on the
///     correct cardinal tiles and the four corners on the diagonals, and it must clear the
///     console's power lock once every part is linked.
/// </summary>
[TestFixture]
public sealed class HFRAssemblyTest
{
    [Test]
    public async Task AnchoringThePatternLinksEveryPart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var mapSys = server.System<SharedMapSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid core = default;
        EntityUid console = default;
        EntityUid fuel = default;
        EntityUid moderator = default;
        EntityUid waste = default;
        var corners = new EntityUid[4];

        await server.WaitAssertion(() =>
        {
            var plating = tiles["Plating"];
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    mapSys.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }

            var center = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));

            // BaseStructure spawns anchored, and each part's startup aligns itself to the core
            // and registers with it. Spawning the core first is therefore enough to build the
            // multiblock without manually poking transforms.
            core = entMan.SpawnEntity("HFRCore", center);
            console = entMan.SpawnEntity("HFRConsole", center.Offset(new Vector2(0, 1)));
            fuel = entMan.SpawnEntity("HFRFuelInput", center.Offset(new Vector2(1, 0)));
            moderator = entMan.SpawnEntity("HFRModeratorInput", center.Offset(new Vector2(-1, 0)));
            waste = entMan.SpawnEntity("HFRWasteOutput", center.Offset(new Vector2(0, -1)));

            corners[0] = entMan.SpawnEntity("HFRCorner", center.Offset(new Vector2(1, 1)));
            corners[1] = entMan.SpawnEntity("HFRCorner", center.Offset(new Vector2(-1, 1)));
            corners[2] = entMan.SpawnEntity("HFRCorner", center.Offset(new Vector2(1, -1)));
            corners[3] = entMan.SpawnEntity("HFRCorner", center.Offset(new Vector2(-1, -1)));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var coreComp = entMan.GetComponent<HFRCoreComponent>(core);

            Assert.Multiple(() =>
            {
                Assert.That(coreComp.ConsoleUid, Is.EqualTo(console), "console was not linked");
                Assert.That(coreComp.FuelInputUid, Is.EqualTo(fuel), "fuel input was not linked");
                Assert.That(coreComp.ModeratorInputUid, Is.EqualTo(moderator), "moderator input was not linked");
                Assert.That(coreComp.WasteOutputUid, Is.EqualTo(waste), "waste output was not linked");
                Assert.That(coreComp.CornerUids, Is.EquivalentTo(corners), "the four corners were not linked");
                Assert.That(server.System<HypertorusFusionReactorSystem>().AreAllPartsConnected(core, coreComp), Is.True,
                    "the core did not report a complete multiblock");
                Assert.That(entMan.GetComponent<HFRConsoleComponent>(console).CoreUid, Is.EqualTo(core),
                    "the console did not point back at the core");
                Assert.That(entMan.GetComponent<ApcPowerReceiverComponent>(console).PowerDisabled, Is.False,
                    "the assembled console should be unlocked");
            });
        });

        await pair.CleanReturnAsync();
    }
}
