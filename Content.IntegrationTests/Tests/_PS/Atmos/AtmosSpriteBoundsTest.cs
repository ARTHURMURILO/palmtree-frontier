#nullable enable
using System.Collections.Generic;
using Content.Shared.Atmos.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS.Atmos;

/// <summary>
/// Palmtree: a client asserts inside <see cref="SpriteSystem.CalculateLocalBounds"/> when a sprite that snaps to
/// cardinals has a layer state with more than one direction. A client measures sprites whenever it lists a
/// prototype - the entity spawn menu and the guidebook's entity embeds both do it - so a sprite like that takes
/// the whole game down rather than just looking wrong.
/// </summary>
/// <remarks>
/// The heat pump's port layers are four-directional, which is fine on its own, but its sprite used to ask for
/// cardinal snapping as well: opening the spawn menu killed the client.
/// </remarks>
[TestFixture]
public sealed class AtmosSpriteBoundsTest
{
    [Test]
    public async Task AtmosMachinesCanMeasureTheirSprites()
    {
        var settings = new PoolSettings { Connected = true }; // measuring a sprite is the client's job
        await using var pair = await PoolManager.GetServerClient(settings);

        var failures = new List<string>();

        await pair.Client.WaitPost(() =>
        {
            var entMan = pair.Client.EntMan;
            var spriteSystem = entMan.System<SpriteSystem>();

            foreach (var proto in pair.Client.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                // Every atmos machine that has pipe layers, which is the family whose sprites have directional
                // states. Vanilla machines are covered too, so a subtraction from base that breaks this shows up.
                if (proto.Abstract || !proto.TryGetComponent("AtmosPipeLayers", out AtmosPipeLayersComponent? _))
                    continue;

                var uid = entMan.Spawn(proto.ID);
                try
                {
                    entMan.RunMapInit(uid, pair.Client.MetaData(uid));

                    var sprite = entMan.GetComponent<SpriteComponent>(uid);

                    // This is what a SpriteView does when the spawn menu or the guidebook lists the prototype.
                    spriteSystem.GetLocalBounds(new Entity<SpriteComponent>(uid, sprite));
                }
                catch (Exception e)
                {
                    failures.Add($"{proto.ID}: {e.GetType().Name}");
                }
                finally
                {
                    entMan.DeleteEntity(uid);
                }
            }
        });

        Assert.That(failures, Is.Empty,
            "These atmos machines cannot have their sprite measured, which crashes any client that lists them " +
            "(the spawn menu, guidebook embeds). A layer state with more than one direction needs the sprite to " +
            "not snap to cardinals.");

        await pair.CleanReturnAsync();
    }
}
