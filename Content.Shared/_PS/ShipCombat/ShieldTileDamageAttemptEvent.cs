using System.Numerics;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: raised on a grid when an explosion is about to destroy its floor tiles. Ship shields
/// can absorb it (cancel the event) instead of letting the hull take the damage.
/// </summary>
[ByRefEvent]
public record struct ShieldTileDamageAttemptEvent(Vector2 Epicenter, float Intensity, bool Cancelled = false);
