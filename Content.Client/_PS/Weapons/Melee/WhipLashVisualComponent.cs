using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client._PS.Weapons.Melee;

/// <summary>
/// Client-only visual state for a whip lash. The entity carrying this component starts at the
/// wielder, extends to <see cref="Target"/>, then retracts and deletes itself.
/// </summary>
[RegisterComponent]
public sealed partial class WhipLashVisualComponent : Component
{
    public EntityUid User;

    /// <summary>
    /// World-space strike point.
    /// </summary>
    public Vector2 Target;

    public MapId Map;

    public TimeSpan StartTime;

    public float ExtendDuration = 0.1f;
    public float HoldDuration = 0.035f;
    public float RetractDuration = 0.12f;
}
