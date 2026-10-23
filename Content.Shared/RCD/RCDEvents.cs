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
/// Sent by the RPD placement mode whenever the local eye rotation changes, because
/// eye rotation is not networked and the server needs it to resolve the pipe layer.
/// </summary>
[Serializable, NetSerializable]
public sealed class RPDEyeRotationEvent(NetEntity netEntity, float? eyeRotation) : EntityEventArgs
{
    public readonly NetEntity NetEntity = netEntity;
    public readonly float? EyeRotation = eyeRotation;
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
