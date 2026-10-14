namespace Content.Server._PS.Salvage.Components;

/// <summary>
/// Palmtree: marks a body recovered onto a shuttle from an expedition. When the shuttle arrives
/// back on the main map, the body's rattle implant message is re-sent with the real coordinates
/// and this component is removed.
/// </summary>
[RegisterComponent]
public sealed partial class ExpedRecoveredBodyComponent : Component
{
}
