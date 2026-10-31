// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._NSV13.Reactor;

// These have to live in Content.Shared and be [NetSerializable]: a DoAfterEvent is part of the
// DoAfterComponent's networked state, so SharedDoAfterSystem asserts that the concrete type carries
// [NetSerializable]. Marking a type [NetSerializable] inside Content.Server would register it with
// NetSerializer on the server only and shift ids out from under the client, which is why moving them
// here is the fix rather than adding the attribute in place.

/// <summary>Raised when the hands-on installation of a control rod finishes.</summary>
[Serializable, NetSerializable]
public sealed partial class StormdriveRodInstallDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>Raised when pulling a mounted control rod out of the bay finishes.</summary>
[Serializable, NetSerializable]
public sealed partial class StormdriveRodRemoveDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>Index into the server-side <c>StormdriveComponent.ControlRods</c> list the operator picked.</summary>
    public int Index;
}

/// <summary>Raised when one step of the meltdown recovery chain finishes.</summary>
[Serializable, NetSerializable]
public sealed partial class StormdriveRepairDoAfterEvent : SimpleDoAfterEvent
{
    public StormdriveRepairStep Step;
}

/// <summary>Raised when a fuel rod finishes being loaded into an RBMK.</summary>
[Serializable, NetSerializable]
public sealed partial class RbmkFuelInsertDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>Raised when sealant finishes being applied to an RBMK vessel.</summary>
[Serializable, NetSerializable]
public sealed partial class RbmkSealantDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>
///     One step of the stormdrive's meltdown recovery chain. Shared because
///     <see cref="StormdriveRepairDoAfterEvent"/> carries it over the network.
/// </summary>
[Serializable, NetSerializable]
public enum StormdriveRepairStep : byte
{
    ClearSludge,
    Reline,
    Weld,
    InstallCore,
}
