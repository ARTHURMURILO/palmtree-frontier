// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor.Events;

/// <summary>
///     Raised on a stormdrive when its <see cref="Components.StormdriveComponent.State"/> changes.
/// </summary>
/// <remarks>
///     The rod-bay window is a separate UI from the console and was only refreshed when it opened
///     or when a rod changed, so a maintenance toggle made elsewhere never reached it. A local
///     event keeps the two systems decoupled - the rod system already depends on the drive system,
///     so the reverse dependency would be a cycle.
/// </remarks>
public sealed class StormdriveStateChangedEvent : EntityEventArgs
{
}
