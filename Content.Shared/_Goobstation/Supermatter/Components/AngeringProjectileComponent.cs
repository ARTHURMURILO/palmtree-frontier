// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Goobstation.Supermatter.Components;

/// <summary>
///     Goobstation (ported from Omu's emitter diode/disc)
///     Marks a projectile as one that "angers" a supermatter crystal, contributing
///     integrity damage and/or power instead of just raw projectile damage.
/// </summary>
[RegisterComponent]
public sealed partial class AngeringProjectileComponent : Component
{
    [DataField]
    public float? IntegDamage;

    [DataField]
    public float? EnergyDamage;
}
