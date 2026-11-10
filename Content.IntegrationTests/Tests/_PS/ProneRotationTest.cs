using Content.Shared.Humanoid;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: downed/crawling humanoids lie with their head toward the horizontal movement direction.
/// </summary>
[TestFixture]
public sealed class ProneRotationTest
{
    [Test]
    public async Task CrawlingFlipsHorizontalRotation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var standing = server.System<StandingStateSystem>();
            var transform = server.System<SharedTransformSystem>();
            var rotation = entMan.GetComponent<RotationVisualsComponent>(mob);

            Assert.That(entMan.HasComponent<HumanoidAppearanceComponent>(mob), Is.True);
            Assert.That(standing.Down(mob, playSound: false, dropHeldItems: false, force: true), Is.True);

            transform.SetLocalRotation(mob, Direction.East.ToAngle());
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(270)),
                "moving east must mirror the prone sprite to face right");

            transform.SetLocalRotation(mob, Direction.West.ToAngle());
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(90)),
                "moving west must restore the prone sprite to face left");

            transform.SetLocalRotation(mob, Direction.North.ToAngle());
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(90)),
                "north/south movement keeps the last left/right lie");
        });

        await pair.CleanReturnAsync();
    }
}
