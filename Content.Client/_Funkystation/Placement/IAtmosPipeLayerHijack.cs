// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Construction.Prototypes;
using Robust.Client.Placement;

namespace Content.Client._Funkystation.Placement;

/// <summary>
///     Lets <see cref="Content.Client.Atmos.AlignAtmosPipeLayers"/> rebuild whichever placement
///     hijack is currently active when the mouse selects another atmos pipe layer.
/// </summary>
public interface IAtmosPipeLayerHijack
{
    ConstructionPrototype? CurrentPrototype { get; }

    /// <summary>
    ///     Returns a new hijack of the same type for the given construction recipe.
    /// </summary>
    PlacementHijack WithPrototype(ConstructionPrototype newPrototype);
}
