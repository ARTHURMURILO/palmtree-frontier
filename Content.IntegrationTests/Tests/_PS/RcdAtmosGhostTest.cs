using System.Linq;
using System.Numerics;
using Content.Shared._PS.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.RCD;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Server.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the RPD placement ghost must reject the same node overlaps the server rejects, even
/// though <see cref="Content.Shared.NodeContainer.NodeContainerComponent.Nodes"/> is server-only.
/// </summary>
[TestFixture]
public sealed class RcdAtmosGhostTest
{
    [Test]
    public async Task ClientGhostRejectsPipeNodeOverlap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var testMap = await pair.CreateTestMap();

        var playerMan = server.ResolveDependency<IPlayerManager>();
        var session = playerMan.Sessions.Single();
        var pipeCoords = new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f));
        NetEntity pipeNet = default;

        await server.WaitPost(() =>
        {
            // Attach a player so the map grid and the newly built pipe are in the client's PVS.
            var mob = server.EntMan.SpawnEntity("MobHuman", pipeCoords);
            playerMan.SetAttachedEntity(session, mob);

            var pipe = server.EntMan.SpawnEntity("GasPipeStraight", pipeCoords);
            pipeNet = server.EntMan.GetNetEntity(pipe);
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var pipe = client.EntMan.GetEntity(pipeNet);

            Assert.That(
                client.EntMan.TryGetComponent<AtmosPipeNodeDirectionsComponent>(pipe, out var directions),
                Is.True,
                "The networked pipe node summary did not reach the client.");

            Assert.That(
                (int) (directions.Primary & PipeDirection.North),
                Is.Not.Zero,
                "A straight pipe should expose its north node to the ghost.");

            Assert.That(
                (int) (directions.Primary & PipeDirection.South),
                Is.Not.Zero,
                "A straight pipe should expose its south node to the ghost.");
        });

        // The first raise requests the prototype's node data from the server; the reply is cached.
        await client.WaitAssertion(() =>
        {
            var proto = client.ProtoMan.Index<EntityPrototype>("GasPipeStraight");
            var ev = new RCDAtmosOverlapAttemptEvent(proto, testMap.CGridCoords, Angle.Zero);
            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        });

        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var sameLayer = new RCDAtmosOverlapAttemptEvent(
                client.ProtoMan.Index<EntityPrototype>("GasPipeStraight"),
                testMap.CGridCoords,
                Angle.Zero);

            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref sameLayer);

            Assert.That(
                sameLayer.Cancelled,
                Is.True,
                "The ghost should reject a same-layer straight pipe whose nodes overlap the anchored pipe.");

            var otherLayer = new RCDAtmosOverlapAttemptEvent(
                client.ProtoMan.Index<EntityPrototype>("GasPipeStraightAlt1"),
                testMap.CGridCoords,
                Angle.Zero);

            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref otherLayer);

            Assert.That(
                otherLayer.Cancelled,
                Is.False,
                "A secondary-layer pipe does not conflict with a primary-layer pipe.");
        });
    }

    [Test]
    public async Task ServerSummaryFollowsLayerChanges()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        EntityUid pipe = default;

        await server.WaitAssertion(() =>
        {
            pipe = server.EntMan.SpawnEntity(
                "GasPipeStraight",
                new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f)));

            var summary = server.EntMan.GetComponent<AtmosPipeNodeDirectionsComponent>(pipe);

            Assert.Multiple(() =>
            {
                Assert.That((int) (summary.Primary & PipeDirection.North), Is.Not.Zero);
                Assert.That((int) (summary.Primary & PipeDirection.South), Is.Not.Zero);
                Assert.That((int) summary.Secondary, Is.Zero);
            });

            var pipeLayers = server.EntMan.GetComponent<AtmosPipeLayersComponent>(pipe);
            server.System<SharedAtmosPipeLayersSystem>().SetPipeLayer((pipe, pipeLayers), AtmosPipeLayer.Secondary);

            Assert.Multiple(() =>
            {
                Assert.That((int) summary.Primary, Is.Zero, "Primary directions should be cleared after a layer change.");
                Assert.That((int) (summary.Secondary & PipeDirection.North), Is.Not.Zero);
                Assert.That((int) (summary.Secondary & PipeDirection.South), Is.Not.Zero);
            });
        });
    }
}
