using Content.Shared._PS.Traits;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.EntityEffects;

/// <summary>
/// Palmtree: passes only for characters carrying <see cref="TheobromineIntoleranceComponent"/>
/// (the Theobromine intolerance trait). Replaces the old species/metabolizer check so the
/// intolerance is opt-in per character instead of baked into the species.
/// </summary>
public sealed partial class TheobromineIntolerance : EntityEffectCondition
{
    public override bool Condition(EntityEffectBaseArgs args)
    {
        return args.EntityManager.TryGetComponent<TheobromineIntoleranceComponent>(args.TargetEntity, out _);
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        return Loc.GetString("reagent-effect-condition-guidebook-theobromine-intolerance");
    }
}
