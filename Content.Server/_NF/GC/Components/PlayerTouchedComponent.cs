namespace Content.Server._NF.GC.Components;

/// <summary>
/// Denotes an entity that a player has picked up at some point.
/// The deletion census uses this to keep loot players have handled from being culled,
/// and to rescue it from grids that are culled.
/// </summary>
[RegisterComponent]
public sealed partial class PlayerTouchedComponent : Component
{
}
