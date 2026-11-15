// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     Marks the packed reactor core item used to refit a wrecked stormdrive.
/// </summary>
/// <remarks>
///     The DM version tested <c>istype(I, /obj/item/stormdrive_core)</c>. SS14 prototypes cannot be
///     used as types, so the item carries a marker component instead.
/// </remarks>
[RegisterComponent]
public sealed partial class StormdriveCoreComponent : Component
{
}
