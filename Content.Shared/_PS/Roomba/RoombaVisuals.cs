using Robust.Shared.Serialization;

namespace Content.Shared._PS.Roomba;

/// <summary>
/// Palmtree: appearance keys used to dim the roomba while it is switched off.
/// </summary>
[Serializable, NetSerializable]
public enum RoombaVisuals : byte
{
    Enabled,
}

/// <summary>
/// Palmtree: sprite layer map keys for <see cref="RoombaVisuals"/>.
/// </summary>
[Serializable, NetSerializable]
public enum RoombaVisualLayers : byte
{
    Base,
}
