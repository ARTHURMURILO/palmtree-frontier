namespace Content.Server._NF.GC.Components;

/// <summary>
/// Denotes an entity that a player has picked up at some point.
/// The deletion census uses this to keep loot players have handled from being culled,
/// and to rescue it from grids that are culled.
/// Protection lapses a configurable time after the entity is dropped.
/// </summary>
[RegisterComponent]
public sealed partial class PlayerTouchedComponent : Component
{
    /// <summary>
    /// When the census protection for this entity expires, or null if it does not
    /// (i.e. the entity is currently held and has not been dropped since).
    /// </summary>
    [DataField]
    public TimeSpan? ProtectionExpiry;
}
