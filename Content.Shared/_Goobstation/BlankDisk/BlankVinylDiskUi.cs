using Robust.Shared.Serialization;

namespace Content.Shared._Goobstation.BlankDisk;

[Serializable, NetSerializable]
public enum BlankVinylDiskUiKey : byte
{
    Key,
}

/// <summary>
/// Client asks to imprint the blank disk into the given vinyl disk prototype.
/// </summary>
[Serializable, NetSerializable]
public sealed class BlankVinylDiskSelectMessage : BoundUserInterfaceMessage
{
    public readonly string Prototype;

    public BlankVinylDiskSelectMessage(string prototype)
    {
        Prototype = prototype;
    }
}
