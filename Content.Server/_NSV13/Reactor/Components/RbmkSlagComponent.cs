// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     Marks a chunk of corium thrown out by a reactor failure.
/// </summary>
/// <remarks>
///     Exists so the rest of the code can ask "what slag is on this ship?" without string
///     matching a prototype id: the meltdown path counts them, and cleanup/repair code will want
///     to find them too. The actual hazard lives in <c>RadiationSourceComponent</c>.
/// </remarks>
[RegisterComponent]
public sealed partial class RbmkSlagComponent : Component;
