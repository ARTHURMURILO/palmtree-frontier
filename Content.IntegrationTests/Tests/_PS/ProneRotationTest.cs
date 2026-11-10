using Content.Shared.Humanoid;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: downed/crawling humanoids choose a lying direction when they fall and keep it until
/// they stand up.
/// </summary>
[TestFixture]
public sealed class ProneRotationTest
{
    [Test]
    public async Task LayDirectionIsLockedUntilStanding()
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

            // Fall while facing east: head right.
            transform.SetLocalRotation(mob, Direction.East.ToAngle());
            Assert.That(standing.Down(mob, playSound: false, dropHeldItems: false, force: true), Is.True);
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(270)));

            // Facing west while already down must not flip the body.
            transform.SetLocalRotation(mob, Direction.West.ToAngle());
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(270)),
                "the lying direction must not change while prone");

            // Stand, face west, fall again: head left.
            Assert.That(standing.Stand(mob, force: true), Is.True);
            transform.SetLocalRotation(mob, Direction.West.ToAngle());
            Assert.That(standing.Down(mob, playSound: false, dropHeldItems: false, force: true), Is.True);
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(90)));

            // North/south uses the default side lie and still locks.
            transform.SetLocalRotation(mob, Direction.East.ToAngle());
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(Angle.FromDegrees(90)),
                "the lying direction must not change while prone");

            Assert.That(standing.Stand(mob, force: true), Is.True);
            transform.SetLocalRotation(mob, Direction.North.ToAngle());
            Assert.That(standing.Down(mob, playSound: false, dropHeldItems: false, force: true), Is.True);
            Assert.That(rotation.HorizontalRotation, Is.EqualTo(rotation.DefaultRotation));
        });

        await pair.CleanReturnAsync();
    }
}
