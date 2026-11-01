// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Goobstation.Supermatter.Components;

/// <summary>
///     Marks a singularity that does not pick up a level-based energy drain.
/// </summary>
/// <remarks>
///     Vanilla re-derives <c>EnergyDrain</c> from the singularity's level whenever that level changes,
///     which overwrites whatever the prototype asked for. A level 1 singularity therefore always
///     loses one energy per second, and one that is left with nothing to eat is deleted after three
///     minutes. That is the right default for a contained engine; it is the wrong one for a
///     supermatter collapse, whose whole point is the hole. This marker keeps the prototype's own
///     drain, so the crystal's singularity persists until the danger tether takes it.
/// </remarks>
[RegisterComponent]
public sealed partial class PersistentSingularityComponent : Component;
