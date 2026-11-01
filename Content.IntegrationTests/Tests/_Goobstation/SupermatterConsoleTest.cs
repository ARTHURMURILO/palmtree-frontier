// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Linq;
using Content.Shared._Goobstation.Supermatter.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Goobstation;

/// <summary>
///     Palmtree: the monitoring console is per-ship, and a crystal's own window (the admin-ghost
///     inspection path) is per-crystal.
/// </summary>
[TestFixture]
public sealed class SupermatterConsoleTest
{
    [Test]
    public async Task MonitoringIsPerShipAndACrystalOnlyShowsItself()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();

        // Two ships flying next to each other.
        var shipA = await pair.CreateTestMap();
        var shipB = await pair.CreateTestMap();

        EntityUid console = default;
        EntityUid crystalA = default;
        EntityUid shard = default;
        EntityUid crystalB = default;
        EntityUid actor = default;

        await server.WaitPost(() =>
        {
            // CreateTestMap paves only (0,0). Give the ship a few more tiles so the crystals do not
            // spawn on top of each other - an unanchored shard that touches another crystal is
            // consumed by it.
            var mapSystem = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"];
            var tile = new Tile(plating.TileId);

            for (var x = 0; x < 3; x++)
                mapSystem.SetTile(shipA.Grid, new EntityCoordinates(shipA.Grid, x, 0), tile);

            console = entMan.SpawnEntity("ComputerSupermatter", new EntityCoordinates(shipA.Grid, 0, 0));
            crystalA = entMan.SpawnEntity("Supermatter", new EntityCoordinates(shipA.Grid, 0, 0));
            shard = entMan.SpawnEntity("SupermatterSmall", new EntityCoordinates(shipA.Grid, 2, 0));
            crystalB = entMan.SpawnEntity("Supermatter", shipB.GridCoords);

            // A real mob: the engine's UI input validation requires the actor to be able to perform
            // complex interactions, and a bare entity is refused.
            actor = entMan.SpawnEntity("MobHuman", new EntityCoordinates(shipA.Grid, 1, 0));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryOpenUi(console, SupermatterConsoleUiKey.Key, actor), Is.True,
                "the monitoring console should open");
            Assert.That(ui.TryOpenUi(shard, SupermatterConsoleUiKey.Key, actor), Is.True,
                "a crystal should open its own window");
        });

        // Let the console system push a state.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // The monitoring console lists the installed crystals of its own ship only. The other
            // ship's crystal must never appear, and neither must the shard that is still loose in
            // its crate (it is not installed yet).
            var consoleState = GetState(ui, console);
            Assert.That(consoleState, Is.Not.Null, "the console never pushed a UI state");
            Assert.That(consoleState!.SelfInspection, Is.False);
            Assert.That(consoleState.Supermatters.Select(entry => entry.NetEntity),
                Is.EquivalentTo(new[] { entMan.GetNetEntity(crystalA) }),
                "a console must only list the installed crystals of its own grid");

            // A crystal acting as its own console reports exactly one entry - itself - with its own
            // stats, even though it is not anchored yet.
            var shardState = GetState(ui, shard);
            Assert.That(shardState, Is.Not.Null, "the crystal never pushed a UI state");
            Assert.That(shardState!.SelfInspection, Is.True);
            Assert.That(shardState.Supermatters, Has.Length.EqualTo(1));
            Assert.That(shardState.Supermatters[0].NetEntity, Is.EqualTo(entMan.GetNetEntity(shard)));
            Assert.That(shardState.FocusData, Is.Not.Null, "an unanchored shard must still report its stats");
            Assert.That(shardState.FocusData!.Value.NetEntity, Is.EqualTo(entMan.GetNetEntity(shard)));

            // And it is drawn with its own art, not the full crystal's.
            Assert.That(shardState.Supermatters[0].Icon, Is.InstanceOf<SpriteSpecifier.Rsi>());
            var shardIcon = (SpriteSpecifier.Rsi) shardState.Supermatters[0].Icon;
            Assert.That(shardIcon.RsiPath.ToString(), Does.Contain("supermatter_small"));

            // Both windows travel to the client as BUI states; round-trip them so a field the net
            // serializer cannot handle fails here instead of in a live round.
            var serializer = server.ResolveDependency<IRobustSerializer>();
            Serialize(serializer, consoleState);
            Serialize(serializer, shardState);
        });

        await pair.CleanReturnAsync();
    }

    private static SupermatterConsoleBoundInterfaceState? GetState(UserInterfaceSystem ui, EntityUid uid) =>
        ui.TryGetUiState<SupermatterConsoleBoundInterfaceState>(uid, SupermatterConsoleUiKey.Key, out var state)
            ? state
            : null;

    private static void Serialize<T>(IRobustSerializer serializer, T value)
    {
        using var stream = new MemoryStream();
        serializer.Serialize(stream, value!);
        stream.Position = 0;
        var copy = serializer.Deserialize<T>(stream);
        Assert.That(copy, Is.Not.Null);
    }
}
