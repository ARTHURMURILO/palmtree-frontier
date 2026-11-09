#nullable enable
using System.Linq;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

[TestFixture]
public sealed class CircuitrySecurityTest
{
    [Test]
    public async Task LockClaimsOwnerAndRejectsOthers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var session = playerMan.Sessions.Single();

        EntityUid player = default;
        EntityUid assembly = default;
        EntityUid bystander = default;

        await server.WaitPost(() =>
        {
            player = entMan.SpawnEntity("MobHuman", map.GridCoords);
            playerMan.SetAttachedEntity(session, player);
            assembly = entMan.SpawnEntity("CircuitAssembly", map.GridCoords);
            bystander = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var assemblySystem = entMan.System<CircuitAssemblySystem>();
            var component = entMan.GetComponent<CircuitAssemblyComponent>(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(assemblySystem.TrySetLocked(assembly, player, true), Is.True);
                Assert.That(component.Locked, Is.True);
                Assert.That(component.OwnerName, Is.EqualTo(session.Name));
                Assert.That(assemblySystem.CanModify(assembly, player), Is.True);
                Assert.That(assemblySystem.CanModify(assembly, bystander), Is.False);
                Assert.That(assemblySystem.TrySetLocked(assembly, bystander, false), Is.False);
                Assert.That(assemblySystem.TrySetLocked(assembly, player, false), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }
}
