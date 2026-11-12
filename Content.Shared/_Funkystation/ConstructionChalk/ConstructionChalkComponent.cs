// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Funkystation.ConstructionChalk;

// tool that places construction chalk marks
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ConstructionChalkComponent : Component
{
    // which set of categories the radial shows
    [DataField, AutoNetworkedField]
    public ChalkMode Mode = ChalkMode.Construction;

    [DataField]
    public SoundSpecifier PlaceSound = new SoundCollectionSpecifier("Chalk");

    [ViewVariables]
    public bool IsStatusControlUpdateRequired;

    /// <summary>
    ///     Server-side rate limit for network placement requests.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextPlace;
}
