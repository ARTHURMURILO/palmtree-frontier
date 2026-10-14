using Robust.Shared.Serialization;

namespace Content.Shared._PS.Shuttles.Events;

/// <summary>
/// Palmtree: raised on the client when it wishes to attempt docking all of a shuttle's free docking
/// ports at once (the "Dock All" button on the shuttle console).
/// The server pairs each listed port with a compatible dock and docks it; ports that cannot dock
/// are skipped, mirroring what clicking "Dock" on them individually would do.
/// </summary>
[Serializable, NetSerializable]
public sealed class DockAllRequestMessage : BoundUserInterfaceMessage
{
    public List<NetEntity> DockEntities;

    public DockAllRequestMessage(List<NetEntity> dockEntities)
    {
        DockEntities = dockEntities;
    }
}
