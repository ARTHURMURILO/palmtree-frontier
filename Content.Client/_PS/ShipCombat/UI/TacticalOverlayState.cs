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

    /// <summary>Ships that are locking this one (yellow cursor; flashing when they are armed).</summary>
    public List<TacticalLockCursor> LockCursors { get; } = new();
}

/// <summary>
/// Palmtree: one torpedo blip on the tactical IFF.
/// </summary>
/// <param name="Position">Map position of the torpedo.</param>
/// <param name="Friendly">True when the torpedo was launched by this ship (green), false for other ships (red).</param>
public readonly record struct TacticalTorpedoBlip(Vector2 Position, bool Friendly);

/// <summary>
/// Palmtree: a ship locking this one, drawn with a yellow cursor (flashing when the locker is armed).
/// </summary>
/// <param name="Position">Map position of the locking grid.</param>
/// <param name="Armed">True when the locking ship is not on MASTER SAFE or is at YELLOW+ condition.</param>
public readonly record struct TacticalLockCursor(Vector2 Position, bool Armed);
