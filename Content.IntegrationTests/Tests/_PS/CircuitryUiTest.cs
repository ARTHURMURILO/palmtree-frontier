#nullable enable
using System.Linq;
using Content.Server._PS.Circuitry.Components;
using Content.Server.Hands.Systems;
using Content.Shared._PS.Circuitry;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitryUiTest
{
    [Test]
    public async Task AssemblyAndCpuUisCanOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        var playerMan = server.ResolveDependency<IPlayerManager>();
        var session = playerMan.Sessions.Single();
        var map = await pair.CreateTestMap();

        EntityUid player = default;
        EntityUid assembly = default;
        EntityUid cpu = default;
        EntityUid handCpu = default;

        await server.WaitPost(() =>
        {
            player = entMan.SpawnEntity("MobHuman", map.GridCoords);
            playerMan.SetAttachedEntity(session, player);
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            cpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            handCpu = entMan.SpawnEntity("CircuitCpuBasic", map.GridCoords);
            entMan.GetComponent<CircuitCpuComponent>(cpu).Source = "-- installed cpu";
            entMan.GetComponent<CircuitCpuComponent>(handCpu).Source = "-- hand cpu";
            entMan.System<ItemSlotsSystem>().TryInsert(assembly, "cpu", cpu, null);
            Assert.That(entMan.System<HandsSystem>().TryPickupAnyHand(player, handCpu), Is.True);
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var ui = entMan.System<UserInterfaceSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(ui.TryOpenUi(assembly, CircuitAssemblyUiKey.Key, player), Is.True);
                Assert.That(ui.IsUiOpen(assembly, CircuitAssemblyUiKey.Key, player), Is.True);
                Assert.That(ui.TryOpenUi(assembly, CircuitCpuUiKey.Key, player), Is.True);
                Assert.That(ui.IsUiOpen(assembly, CircuitCpuUiKey.Key, player), Is.True);
                Assert.That(ui.TryOpenUi(handCpu, CircuitCpuUiKey.Key, player), Is.True);
                Assert.That(ui.IsUiOpen(handCpu, CircuitCpuUiKey.Key, player), Is.True);

                // The installed CPU editor must receive its source on open; otherwise the client
                // cannot track dirty state and the Save button stays disabled.
                Assert.That(ui.TryGetUiState<CircuitCpuState>(assembly, CircuitCpuUiKey.Key, out var installedState), Is.True);
                Assert.That(installedState!.Source, Is.EqualTo("-- installed cpu"));
                Assert.That(installedState.Installed, Is.True);

                Assert.That(ui.TryGetUiState<CircuitCpuState>(handCpu, CircuitCpuUiKey.Key, out var heldState), Is.True);
                Assert.That(heldState!.Source, Is.EqualTo("-- hand cpu"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
