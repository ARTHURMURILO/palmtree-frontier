using System.Numerics;
using Content.Client.Construction;
using Content.Client.Hands.Systems;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Content.Shared.Tiles;
using Robust.Client.Placement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the RPD ghost and the server must agree on the construction direction, even when the
/// placement manager retains a direction from the previous tool.
/// </summary>
[TestFixture]
public sealed class RcdGhostDirectionTest : InteractionTest
{
    /// <summary>
    /// Ticks until the server's networked construction direction matches, so the test does not
    /// depend on a system network message arriving within an arbitrary number of ticks.
    /// </summary>
    private async Task WaitForDirection(EntityUid rpd, Direction expected)
    {
        for (var i = 0; i < 60; i++)
        {
            if (SEntMan.GetComponent<RCDComponent>(rpd).ConstructionDirection == expected)
                return;

            await RunTicks(1);
        }
    }

    [Test]
    public async Task SpawnMenuPlacementSurvivesTheGhostUpdate()
    {
        var placement = Client.ResolveDependency<IPlacementManager>();

        // Hold an RPD first: the regression only happened when the player had an RCD/RPD in hand
        // while a sandbox spawn-panel placement was active.
        await PlaceInHands("RPD");
        await RunTicks(5);

        await Client.WaitPost(() =>
        {
            placement.BeginPlacing(new PlacementInformation
            {
                EntityType = "WallSolid",
                PlacementOption = "SnapgridCenter",
                Range = 2,
            });
        });

        Assert.That(placement.IsActive, Is.True, "Starting a spawn-menu placement should activate it.");

        // Sandbox and map-editor placements leave MobUid at EntityUid.Invalid and carry no hijack.
        // The RCD ghost system must leave those alone, or the spawn panel entry deselects on the
        // next frame.
        await RunTicks(10);

        Assert.Multiple(() =>
        {
            Assert.That(placement.IsActive, Is.True,
                "The RCD ghost system must not clear spawn-panel placements that have no owning mob.");
            Assert.That(placement.CurrentPermission?.PlacementOption, Is.EqualTo("SnapgridCenter"),
                "The spawn-panel placement must not be replaced by the RPD placer.");
        });
    }

    [Test]
    public async Task ReplacingTheRpdSyncsTheRetainedPlacementDirection()
    {
        var placement = Client.ResolveDependency<IPlacementManager>();

        var firstRpdNet = await PlaceInHands("RPD");
        var firstRpd = SEntMan.GetEntity(firstRpdNet);

        // Let the ghost system create the layered placer for the first RPD.
        await RunTicks(5);
        Assert.That(placement.IsActive, Is.True, "Equipping an RPD should start its placement mode.");

        // Rotate once, like pressing the rotate key. This is the direction the ghost now shows.
        await Client.WaitPost(() => placement.Direction = Direction.East);
        await WaitForDirection(firstRpd, Direction.East);

        Assert.That(
            SEntMan.GetComponent<RCDComponent>(firstRpd).ConstructionDirection,
            Is.EqualTo(Direction.East),
            "The first RPD should have received the rotated placement direction.");

        // Swap in a fresh RPD. The old tool is deleted while it owns the placer, so the ghost
        // system must clear that stale placer first. The placement manager then keeps East, while
        // the new component starts at its default South; the ghost must forward East to it.
        var secondRpdNet = await PlaceInHands("RPD");
        var secondRpd = SEntMan.GetEntity(secondRpdNet);
        SEntMan.EventBus.RaiseLocalEvent(secondRpd, new RCDSystemMessage("PipeStraight"));
        await RunTicks(1);

        await WaitForDirection(secondRpd, Direction.East);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Direction, Is.EqualTo(Direction.East),
                "The placement manager should retain its direction across tools.");
            Assert.That(
                SEntMan.GetComponent<RCDComponent>(secondRpd).ConstructionDirection,
                Is.EqualTo(Direction.East),
                "A freshly equipped RPD must use the direction the ghost is showing.");
        });

        // Place a pipe on the empty tile. The spawn has to use the direction the ghost showed,
        // not the fresh component's default South. The RCD needs a grid-local click location:
        // a map-local one makes GetGrid() return null and the operation is refused.
        var target = MapData.GridCoords.Offset(new Vector2(1.5f, 0.5f));
        await SetTile("Plating", SEntMan.GetNetCoordinates(target));
        await Interact(null, target);
        await RunTicks(5);

        EntityUid? pipe = null;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID != "GasPipeStraight" || xform.GridUid != MapData.Grid.Owner)
                continue;

            pipe = uid;
            break;
        }

        Assert.That(pipe, Is.Not.Null, "The RPD should have built a pipe on the empty tile.");
        Assert.That(
            SEntMan.GetComponent<TransformComponent>(pipe!.Value).LocalRotation,
            Is.EqualTo(Direction.East.ToAngle()),
            "The built pipe should face the direction the ghost was showing.");
    }

    [Test]
    public async Task StaleNonRcdPlacerDoesNotBlockTheRpd()
    {
        var placement = Client.ResolveDependency<IPlacementManager>();

        var rpdNet = await PlaceInHands("RPD");
        var rpd = SEntMan.GetEntity(rpdNet);
        SEntMan.EventBus.RaiseLocalEvent(rpd, new RCDSystemMessage("PipeStraight"));
        await RunTicks(5);

        Assert.That(placement.IsActive, Is.True, "Equipping an RPD should start its placement mode.");

        // Simulate a construction-menu placer left active with no owning mob. This is what happens
        // after opening the build menu and then switching to the RPD. The construction menu sets a
        // ConstructionPlacementHijack; the sandbox spawn panel does not, so this is the shape the
        // RCD ghost system is allowed to take over.
        await Client.WaitPost(() =>
        {
            placement.BeginPlacing(
                new PlacementInformation
                {
                    EntityType = "WallSolid",
                    PlacementOption = "SnapgridCenter",
                    Range = 2,
                },
                new ConstructionPlacementHijack(CConSys, null));
        });

        await RunTicks(5);

        Assert.That(
            placement.CurrentPermission?.MobUid,
            Is.EqualTo(ToClient(rpdNet)),
            "Holding an RPD must take over a stale non-RCD placer instead of being ignored forever.");

        // The click must go through to the RPD, not the stale placer.
        var target = MapData.GridCoords.Offset(new Vector2(1.5f, 0.5f));
        await SetTile("Plating", SEntMan.GetNetCoordinates(target));
        await Interact(null, target);
        await RunTicks(5);

        EntityUid? pipe = null;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == "GasPipeStraight" && xform.GridUid == MapData.Grid.Owner)
            {
                pipe = uid;
                break;
            }
        }

        Assert.That(pipe, Is.Not.Null, "Clicking with the RPD should build a pipe after taking over the placer.");
        Assert.That(placement.CurrentPermission?.MobUid, Is.EqualTo(ToClient(rpdNet)));
    }

    [Test]
    public async Task ProtectedGridRefusalIsVisibleOnTheClient()
    {
        await PlaceInHands("RCD");

        await Server.WaitPost(() =>
        {
            var prot = SEntMan.AddComponent<ProtectedGridComponent>(MapData.Grid.Owner);
            typeof(ProtectedGridComponent)
                .GetField(nameof(ProtectedGridComponent.PreventRCDUse))!
                .SetValue(prot, true);
            SEntMan.Dirty(MapData.Grid.Owner, prot);
        });

        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var prot = CEntMan.GetComponent<ProtectedGridComponent>(MapData.CGridUid);
            Assert.That(prot.PreventRCDUse, Is.True,
                "The protected-grid RCD flag must be networked to the client.");

            var player = CEntMan.GetEntity(Player);
            var clientHands = CEntMan.System<Content.Client.Hands.Systems.HandsSystem>();
            var held = clientHands.GetActiveItem(player);
            Assert.That(held, Is.Not.Null, "The client player should be holding the RCD.");
            var rcd = CEntMan.GetComponent<RCDComponent>(held!.Value);
            var rcdSystem = CEntMan.System<RCDSystem>();
            Assert.That(
                rcdSystem.IsRcdUseAllowedOnGrid(MapData.CGridUid, player, rcd),
                Is.False,
                "The client ghost must see the protected-grid refusal for a normal RCD.");
        });
    }
}
