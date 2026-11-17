// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Funkystation.ConstructionChalk;

[Serializable, NetSerializable]
public enum ChalkMode : byte
{
    Construction = 0,
    Piping = 1,
}
