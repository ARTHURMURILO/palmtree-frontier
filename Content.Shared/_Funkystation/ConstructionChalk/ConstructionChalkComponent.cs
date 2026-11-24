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

    /// <summary>
    ///     Maximum number of chalk marks that may be active in the world at once. Chalk is cheap and
    ///     a mark is an entity, so an uncapped placement would let someone flood the entity table.
    ///     Checked against the total mark count whenever this chalk places a new one.
    /// </summary>
    [DataField]
    public int MaxMarks = 128;

    /// <summary>
    ///     How long the marks this chalk places stay around. Marks expire so forgotten plans cannot
    ///     pile up indefinitely. Zero or negative keeps marks forever.
    /// </summary>
    [DataField]
    public TimeSpan MarkLifetime = TimeSpan.FromMinutes(15);

    [ViewVariables]
    public bool IsStatusControlUpdateRequired;

    /// <summary>
    ///     Server-side rate limit for network placement requests.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextPlace;
}
