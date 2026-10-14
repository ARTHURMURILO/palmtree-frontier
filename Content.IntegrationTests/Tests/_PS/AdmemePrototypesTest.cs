using System.Numerics;
using Content.Shared.Audio;
using Content.Shared.Power.Generator;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the admin meme items (formerly the maintainer's roundly admin upload) load and spawn,
/// and their sound collection points at files that exist.
/// </summary>
[TestFixture]
public sealed class AdmemePrototypesTest
{
    private static readonly string[] AdmemePrototypes =
    [
        "PortableGeneratorBlyatman",
        "MobDimitry",
        "DrinkBottleVodkaInfinite",
        "FoodKebabSkewerDeath",
        "FoodPotatoInfinite",
        "ClothingHandsGlovesRedStar",
    ];

    [Test]
    public async Task AdmemeItemsSpawn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in AdmemePrototypes)
            {
                var mob = entMan.SpawnEntity(id, testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
                Assert.That(entMan.EntityExists(mob), Is.True, $"{id} should spawn.");

                // Every meme item is meant to stay admin-only: no in-game source references them.
                var proto = server.ResolveDependency<IPrototypeManager>().Index<EntityPrototype>(id);
                Assert.That(proto.Abstract, Is.False, $"{id} must be spawnable by admins.");
            }

            // The generator's fuel adapter burns vodka and the hum collection is wired up.
            var generator = entMan.SpawnEntity("PortableGeneratorBlyatman", testMap.GridCoords);
            var fuel = entMan.GetComponent<FuelGeneratorComponent>(generator);
            Assert.That(fuel.TargetPower, Is.EqualTo(190000f));
            Assert.That(entMan.GetComponent<AmbientSoundComponent>(generator).Sound, Is.Not.Null);
        });

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var res = server.ResolveDependency<IResourceManager>();
            var collection = proto.Index<SoundCollectionPrototype>("BlyatmanGeneratorHum");

            Assert.That(collection.PickFiles, Is.Not.Empty, "The generator hum collection needs files.");
            foreach (var file in collection.PickFiles)
            {
                Assert.That(res.ContentFileExists(file), Is.True, $"Missing sound file: {file}");
            }
        });

        await pair.CleanReturnAsync();
    }
}
