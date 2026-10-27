using Robust.Shared.Serialization;

namespace Content.Shared.Tabletop
{
    [Serializable, NetSerializable]
    public enum TabletopItemVisuals : byte
    {
        Scale,
        DrawDepth,
        Prototype // Palmtree: prototype id whose sprite a tabletop hologram mimics.
    }
}
