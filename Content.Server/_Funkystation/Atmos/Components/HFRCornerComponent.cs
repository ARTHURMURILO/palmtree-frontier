// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus <90893484+LaCumbiaDelCoronavirus@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 marc-pelletier <113944176+marc-pelletier@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Content.Server._Funkystation.Atmos.Systems;
using System.Linq;

namespace Content.Server._Funkystation.Atmos.Components
{
    [RegisterComponent]
    public sealed partial class HFRCornerComponent : Component
    {
        // Palmtree: runtime link only. It is rebuilt from the anchored parts on startup and cleared
        // on shutdown; serialising entity ids lets a save/load resurrect a dead or recycled UID.
        [ViewVariables(VVAccess.ReadWrite)]
        public EntityUid? CoreUid { get; set; }

        [DataField("isActive")]
        [ViewVariables(VVAccess.ReadWrite)]
        public bool IsActive;

        [DataField("cracked")]
        [ViewVariables(VVAccess.ReadWrite)]
        public bool Cracked;
    }
}