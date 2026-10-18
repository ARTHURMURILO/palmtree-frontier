using System.Collections.Generic;
using System.Numerics;

namespace Content.Client._PS.ShipCombat.UI;

/// <summary>
/// Palmtree: data the tactical console draws on top of the radar.
/// </summary>
public sealed class TacticalOverlayState
{
    /// <summary>Map position of the locked grid, if any.</summary>
    public Vector2? LockTargetPosition;

    /// <summary>Torpedoes visible to this console.</summary>
    public List<TacticalTorpedoBlip> Torpedoes { get; } = new();
}

/// <summary>
/// Palmtree: one torpedo blip on the tactical IFF.
/// </summary>
/// <param name="Position">Map position of the torpedo.</param>
/// <param name="Friendly">True when the torpedo was launched by this ship (green), false for other ships (red).</param>
public readonly record struct TacticalTorpedoBlip(Vector2 Position, bool Friendly);
