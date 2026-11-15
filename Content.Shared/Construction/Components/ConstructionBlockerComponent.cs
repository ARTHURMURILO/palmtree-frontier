// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared.Construction.Components;

/// <summary>
///     Reserves an entity's fixture footprint for construction purposes without making it solid.
/// </summary>
/// <remarks>
///     Palmtree: a large machine (a 3x3 reactor, say) has to stop girders and walls being built
///     through its art, but must not become a wall itself. <see cref="Conditions.TileNotBlocked"/>
///     counts entities with this component as blocking every tile their fixtures overlap, while
///     ordinary physics (walking over the machine) is untouched.
/// </remarks>
[RegisterComponent]
public sealed partial class ConstructionBlockerComponent : Component
{
}
