// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     Flexi-seal reactor sealant: the paste you smear over a worn RBMK vessel.
/// </summary>
/// <remarks>
///     NSV13 used <c>/obj/item/sealant</c> from the wider codebase. This fork has no such prototype,
///     so one is ported here as a marker component plus a spray-can entity.
/// </remarks>
[RegisterComponent]
public sealed partial class ReactorSealantComponent : Component
{
}
