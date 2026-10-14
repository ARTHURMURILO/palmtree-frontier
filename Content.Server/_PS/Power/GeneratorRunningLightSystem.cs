using Content.Shared._PS.Power;
using Content.Shared.Power.Generator;
using Robust.Shared.GameObjects;

namespace Content.Server._PS.Power;

/// <summary>
/// Palmtree: keeps a marked generator's point light in sync with its running state, so the light is
/// only lit while the generator burns fuel. <see cref="SharedPointLightSystem.SetEnabled"/> early
/// outs when nothing changes, so polling is cheap.
/// </summary>
public sealed class GeneratorRunningLightSystem : EntitySystem
{
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GeneratorRunningLightComponent, FuelGeneratorComponent>();
        while (query.MoveNext(out var uid, out _, out var generator))
        {
            _lights.SetEnabled(uid, generator.On);
        }
    }
}
