namespace Content.Shared.Atmos.Components;

/// <summary>
/// Palmtree: marks a plain atmos pipe segment, as opposed to a vent, pump, sensor or manifold.
/// Two segments on the same atmos layer share the same sprite draw depth and z-fight, so the
/// RCD/RPD refuses to stack them. A device may still be built on a segment: its sprite sits on a
/// different draw depth and the node overlap check decides whether it actually fits.
/// </summary>
[RegisterComponent]
public sealed partial class AtmosPipeSegmentComponent : Component;
