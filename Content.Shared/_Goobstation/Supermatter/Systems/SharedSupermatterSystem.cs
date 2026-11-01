// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Goobstation.Supermatter.Systems;

public abstract class SharedSupermatterSystem : EntitySystem
{
    public enum SuperMatterSound : sbyte
    {
        Aggressive = 0,
        Delam = 1
    }

    public enum DelamType : sbyte
    {
        Explosion = 0,
        Singulo = 1,
        Tesla = 2,
        Cascade = 3 // save for later
    }
}
