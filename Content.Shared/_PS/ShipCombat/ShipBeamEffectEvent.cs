using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: tells clients to draw a ship weapon beam between two map positions.
/// </summary>
/// <remarks>
/// Ship combat is larger scale than the normal PVS bubble: the server sends this with a wide PVS
/// range so beams are visible from far outside the shooter's PVS (the vanilla hitscan flashes are
/// only sent to players whose PVS covers the shot, and their coordinates are grid-relative, so a
/// distant client cannot resolve them).
/// </remarks>
[Serializable, NetSerializable]
public sealed class ShipBeamEffectEvent(NetCoordinates from, NetCoordinates to) : EntityEventArgs
{
    /// <summary>The beam's muzzle position, in map coordinates.</summary>
    public NetCoordinates From { get; } = from;

    /// <summary>The beam's end position (the first thing it hit, or the aim point).</summary>
    public NetCoordinates To { get; } = to;
}
