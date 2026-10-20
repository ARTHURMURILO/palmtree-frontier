using Content.Shared._PS.Protean;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;

namespace Content.Server._PS.Protean;

/// <summary>
/// Minimal refactory metabolism: while unfurled, a Protean slowly consumes its stored metal.
/// </summary>
public sealed class ProteanRefactorySystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ProteanComponent, BodyComponent>();
        while (query.MoveNext(out var uid, out var protean, out var body))
        {
            if (protean.Folded)
                continue;

            if (!_body.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((uid, body), out var refactories))
                continue;

            foreach (var refactory in refactories)
            {
                if (refactory.Comp1.Metal <= 0f)
                    continue;

                refactory.Comp1.Metal = Math.Max(0f, refactory.Comp1.Metal - 0.005f * frameTime);
                Dirty(refactory.Owner, refactory.Comp1);
            }
        }
    }
}
