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
    private static readonly EntProtoId StraightPipe = "GasPipeStraight";
    private static readonly EntProtoId StraightPipeAlt1 = "GasPipeStraightAlt1";

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
        EntityUid pipeEntity = default;
        NetEntity pipeNet = default;

        await server.WaitPost(() =>
        {
            // Attach a player so the map grid and the newly built pipe are in the client's PVS.
            var mob = server.EntMan.SpawnEntity("MobHuman", pipeCoords);
            playerMan.SetAttachedEntity(session, mob);

            pipeEntity = server.EntMan.SpawnEntity("GasPipeStraight", pipeCoords);
            pipeNet = server.EntMan.GetNetEntity(pipeEntity);
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

        // The first raise per prototype requests its node data from the server; the replies are
        // cached. Prefetch both prototypes so the assertions below exercise the comparison itself
        // rather than the one-shot request path.
        await client.WaitAssertion(() =>
        {
            var proto = client.ProtoMan.Index(StraightPipe);
            var ev = new RCDAtmosOverlapAttemptEvent(proto, testMap.CGridCoords, Angle.Zero);
            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);

            var altProto = client.ProtoMan.Index(StraightPipeAlt1);
            var altEv = new RCDAtmosOverlapAttemptEvent(altProto, testMap.CGridCoords, Angle.Zero);
            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref altEv);
        });

        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var sameLayer = new RCDAtmosOverlapAttemptEvent(
                client.ProtoMan.Index(StraightPipe),
                testMap.CGridCoords,
                Angle.Zero);

            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref sameLayer);

            Assert.That(
                sameLayer.Cancelled,
                Is.True,
                "The ghost should reject a same-layer straight pipe whose nodes overlap the anchored pipe.");

            var otherLayer = new RCDAtmosOverlapAttemptEvent(
                client.ProtoMan.Index(StraightPipeAlt1),
                testMap.CGridCoords,
                Angle.Zero);

            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref otherLayer);

            Assert.That(
                otherLayer.Cancelled,
                Is.False,
                "A secondary-layer pipe does not conflict with a primary-layer pipe.");
        });

        // The placement direction is rotated before comparing, so a perpendicular straight pipe
        // (east-west) does not overlap the anchored north-south pipe.
        await client.WaitAssertion(() =>
        {
            var perpendicular = new RCDAtmosOverlapAttemptEvent(
                client.ProtoMan.Index(StraightPipe),
                testMap.CGridCoords,
                Angle.FromDegrees(90));

            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref perpendicular);

            Assert.That(
                perpendicular.Cancelled,
                Is.False,
                "A perpendicular straight pipe should not overlap the anchored pipe's nodes.");
        });

        // The anchored pipe's own rotation must be applied too. Rotate it in place; the matching
        // east-west placement now overlaps its east-west nodes.
        await server.WaitPost(() =>
        {
            server.System<SharedTransformSystem>().SetLocalRotation(pipeEntity, Angle.FromDegrees(90));
        });

        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var anchoredRotated = new RCDAtmosOverlapAttemptEvent(
                client.ProtoMan.Index(StraightPipe),
                testMap.CGridCoords,
                Angle.FromDegrees(90));

            client.EntMan.EventBus.RaiseEvent(EventSource.Local, ref anchoredRotated);

            Assert.That(
                anchoredRotated.Cancelled,
                Is.True,
                "The ghost must rotate the anchored pipe's nodes before comparing directions.");
        });

        await pair.CleanReturnAsync();
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

            // Locked-layer entities (the manifold) keep a layer per node instead of following the
            // component's current layer.
            var manifold = server.EntMan.SpawnEntity(
                "GasPipeManifold",
                new EntityCoordinates(testMap.Grid.Owner, new Vector2(1.5f, 0.5f)));

            var manifoldLayers = server.EntMan.GetComponent<AtmosPipeLayersComponent>(manifold);
            var manifoldSummary = server.EntMan.GetComponent<AtmosPipeNodeDirectionsComponent>(manifold);

            Assert.Multiple(() =>
            {
                Assert.That(manifoldLayers.PipeLayersLocked, Is.True);
                Assert.That((int) (manifoldSummary.Primary & PipeDirection.South), Is.Not.Zero);
                Assert.That((int) (manifoldSummary.Secondary & PipeDirection.South), Is.Not.Zero);
                Assert.That((int) (manifoldSummary.Tertiary & PipeDirection.South), Is.Not.Zero);
            });

            // A locked entity ignores layer changes, so its summary must not move either.
            server.System<SharedAtmosPipeLayersSystem>().SetPipeLayer((manifold, manifoldLayers), AtmosPipeLayer.Secondary);

            Assert.Multiple(() =>
            {
                Assert.That(manifoldLayers.CurrentPipeLayer, Is.EqualTo(AtmosPipeLayer.Primary));
                Assert.That((int) (manifoldSummary.Primary & PipeDirection.South), Is.Not.Zero);
                Assert.That((int) (manifoldSummary.Tertiary & PipeDirection.South), Is.Not.Zero);
            });

            // Spawning onto an uninitialized map must not add the summary (PrototypeSaveTest); the
            // MapInit hook supplies it once the map is initialized.
            var mapSystem = server.System<SharedMapSystem>();
            var mapManager = server.ResolveDependency<IMapManager>();
            mapSystem.CreateMap(out var uninitializedMapId, runMapInit: false);
            var uninitializedGrid = mapManager.CreateGridEntity(uninitializedMapId);
            var uninitializedPipe = server.EntMan.SpawnEntity(
                "GasPipeStraight",
                new EntityCoordinates(uninitializedGrid.Owner, new Vector2(0.5f, 0.5f)));

            Assert.That(
                server.EntMan.HasComponent<AtmosPipeNodeDirectionsComponent>(uninitializedPipe),
                Is.False,
                "An uninitialized map must not gain the summary at spawn time.");

            mapSystem.InitializeMap(uninitializedMapId);

            Assert.That(
                server.EntMan.HasComponent<AtmosPipeNodeDirectionsComponent>(uninitializedPipe),
                Is.True,
                "Map initialization should fill the summary for entities spawned on an uninitialized map.");

            mapSystem.DeleteMap(uninitializedMapId);
        });

        await pair.CleanReturnAsync();
    }
}
