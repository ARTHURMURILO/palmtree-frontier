using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._EE.Supermatter.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the supermatter shard is a movable crystal that gets bolted down with a wrench.
/// That is a legitimate interaction with the machine and must not be treated as an offering
/// to the crystal.
/// </summary>
public sealed class SupermatterShardTest : InteractionTest
{
    [Test]
    public async Task WrenchAnchorsTheShardWithoutEatingTheWrench()
    {
        await SpawnTarget("SupermatterSmall");

        var shard = SEntMan.GetEntity(Target!.Value);
        Assert.That(SEntMan.GetComponent<TransformComponent>(shard).Anchored, Is.False,
            "The shard should spawn unanchored.");

        await InteractUsing("Wrench");

        Assert.That(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands)), Is.Not.Null,
            "Wrenching the shard should not consume the wrench.");
        Assert.That(SEntMan.GetComponent<TransformComponent>(shard).Anchored, Is.True,
            "Wrenching the shard should anchor it.");
        Assert.That(SEntMan.GetComponent<SupermatterComponent>(shard).Activated, Is.False,
            "Bolting the shard down should not wake the crystal.");
    }
}
