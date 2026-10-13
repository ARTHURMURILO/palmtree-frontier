namespace Content.Shared._PS.Nutrition;

/// <summary>
/// Palmtree: global balance knobs for hunger and thirst.
/// </summary>
public static class PalmtreeNutrition
{
    /// <summary>
    /// Hunger and thirst decay is multiplied by this value. The stock rates made characters need to
    /// eat and drink far too often, so all decay (including species/NPC overrides) is 2x slower.
    /// </summary>
    public const float DecayRateMultiplier = 1f / 2f;
}
