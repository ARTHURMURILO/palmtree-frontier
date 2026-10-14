using System.Numerics;
using Content.Shared.Eui;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// State for the ship preview window: the camera entity to look through, the ship's name and its
/// world size (used to pick an initial zoom), and the tick the state was built on.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipPreviewEuiState(NetEntity camera, string shipName, Vector2 size, GameTick tick)
    : EuiStateBase
{
    public readonly NetEntity Camera = camera;
    public readonly string ShipName = shipName;
    public readonly Vector2 Size = size;
    public readonly GameTick Tick = tick;
}
