using Content.Shared.Atmos.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.RCD;

[Serializable, NetSerializable]
public sealed class RCDSystemMessage(ProtoId<RCDPrototype> protoId) : BoundUserInterfaceMessage
{
    public ProtoId<RCDPrototype> ProtoId = protoId;
}

[Serializable, NetSerializable]
public sealed class RCDConstructionGhostRotationEvent(NetEntity netEntity, Direction direction) : EntityEventArgs
{
    public readonly NetEntity NetEntity = netEntity;
    public readonly Direction Direction = direction;
}

// Palmtree: mirror prototype flip (ported from the old Coyote code)
[Serializable, NetSerializable]
public sealed class RCDConstructionGhostFlipEvent(NetEntity netEntity, bool useMirrorPrototype) : EntityEventArgs
{
    public readonly NetEntity NetEntity = netEntity;
    public readonly bool UseMirrorPrototype = useMirrorPrototype;
}

// Palmtree: RPD pipe colouring and pipe layer placement (ported from the old Coyote code)
/// <summary>
/// Sent by the RPD menu when the player picks a different pipe colour. Carries the palette key;
/// the colour itself comes from the RPD's <see cref="Content.Shared.RCD.Components.RCDComponent.PipeColorPalette"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class RCDColorChangeMessage(NetEntity netEntity, string key) : BoundUserInterfaceMessage
{
    public readonly NetEntity NetEntity = netEntity;
    public readonly string Key = key;
}

/// <summary>
/// Sent by the RPD placement mode whenever the layer under the cursor changes. The server's click
/// location is snapped to the tile centre, so it cannot reproduce the client's cursor offset and
/// must be told the layer explicitly.
/// </summary>
[Serializable, NetSerializable]
public sealed class RPDLayerEvent(NetEntity netEntity, AtmosPipeLayer layer) : EntityEventArgs
{
    public readonly NetEntity NetEntity = netEntity;
    public readonly AtmosPipeLayer Layer = layer;
}

// Palmtree: lets the server veto an RCD/RPD placement that would overlap an existing pipe node.
// The RCD owns the shared placement rules, but pipe nodes live on the server, so the shared
// validation cannot compare directions on its own.
/// <summary>
/// Raised by the RCD/RPD before building an entity that carries pipe nodes (pipes, pumps, vents,
/// sensors, ...). The server's <c>PipeRestrictOverlapSystem</c> cancels it when the new entity's
/// pipe nodes would overlap an existing pipe's nodes on the same atmos layer, so that legitimate
/// overlaps - a vent on a pipe, or the same pipe on another layer - still go through. The client's
/// <c>AtmosPipeNodeOverlapClientSystem</c> answers the same event from networked node summaries so
/// the placement ghost turns red too.
/// </summary>
[ByRefEvent]
public record struct RCDAtmosOverlapAttemptEvent(
    EntityPrototype Prototype,
    EntityCoordinates Coordinates,
    Angle Rotation)
{
    public bool Cancelled;
}

/// <summary>
/// Raised on a pipe the RPD has just built, so server-side systems can keep the
/// functional pipe colour (what atmos monitors and the spray painter read) in sync
/// with the appearance datum the shared system already set.
/// </summary>
[ByRefEvent]
public record struct RCDPipeColorAppliedEvent(Color Color);

[Serializable, NetSerializable]
public enum RcdUiKey : byte
{
    Key
}

/// <summary>
/// Separate UI key for the RPD so it opens its own menu (mode radial + pipe colour strip)
/// instead of the RCD's construction radial.
/// </summary>
[Serializable, NetSerializable]
public enum RpdUiKey : byte
{
    Key
}
