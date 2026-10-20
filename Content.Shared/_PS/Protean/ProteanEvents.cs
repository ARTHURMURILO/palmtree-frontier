using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Protean;

public sealed partial class ProteanFoldEvent : InstantActionEvent;

public sealed partial class ProteanUnfoldEvent : InstantActionEvent;

public sealed partial class ProteanLockEvent : InstantActionEvent;

public sealed partial class ProteanInterfaceEvent : InstantActionEvent;

public sealed partial class ProteanLowPowerEvent : InstantActionEvent;

public sealed partial class ProteanHealEvent : InstantActionEvent;

[ByRefEvent]
public record struct ProteanSuitChangedEvent;

[Serializable, NetSerializable]
public sealed partial class ProteanAssimilationDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ProteanRepairDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ProteanMemoryResetDoAfterEvent : SimpleDoAfterEvent;
