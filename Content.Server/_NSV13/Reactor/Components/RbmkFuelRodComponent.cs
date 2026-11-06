// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     A fuel rod for the NSV13 RBMK reactor.
/// </summary>
/// <remarks>
///     Ported from <c>nsv13/code/modules/power/reactor/fuel_rods.dm</c> (245 lines).
///     Rods contribute <see cref="FuelPower"/> to the reactor's reactivity and deplete over time.
/// </remarks>
[RegisterComponent]
public sealed partial class RbmkFuelRodComponent : Component
{
    /// <summary>Accumulated depletion. Reaching <see cref="DepletionThreshold"/> triggers conversion.</summary>
    /// <remarks>
    ///     Palmtree: serialized. A half-burnt rod must stay half-burnt when it is ejected, carried
    ///     around and stored with the ship; otherwise docking resets the fuel for free.
    /// </remarks>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float Depletion;

    /// <summary>Contribution to the reactor's reactivity per tick.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float FuelPower = 0.10f;

    /// <summary>What <see cref="FuelPower"/> decays towards; also the starting value for self-decaying rods.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float OriginalFuelPower = 0.20f;

    /// <summary>
    ///     Radiation strength when held, in rads/s. NSV13's BYOND figures (500 uranium, 1500
    ///     plutonium, 6000 depleted) are divided by 1000 at the boundary; the default matches the
    ///     uranium rod so a prototyped rod that forgets the field is not instantly lethal.
    /// </summary>
    [DataField]
    public float RadStrength = 0.5f;

    /// <summary>
    ///     Rods with this set decay on their own regardless of whether they are in a reactor,
    ///     halving <see cref="FuelPower"/> every <see cref="HalfLife"/> seconds.
    /// </summary>
    [DataField]
    public bool SelfDecay;

    [DataField]
    public float HalfLife = 2000f;

    /// <summary>
    ///     Seconds this rod has existed, used for self-decay. A relative age, not an absolute
    ///     <c>CurTime</c>: ship files outlive rounds, and an absolute round time restored in a later
    ///     round would make the elapsed value negative, which turns decay into a fuel amplifier.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float AgeSeconds;

    /// <summary>Depletion at which <see cref="DepletedFinal"/> is latched and conversion happens.</summary>
    [DataField]
    public float DepletionThreshold = 100f;

    [DataField]
    public float DepletionSpeedModifier = 1f;

    /// <summary>Conversion only ever runs once.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool DepletedFinal;

    /// <summary>Prototype to transform into once depleted, if any.</summary>
    [DataField]
    public EntProtoId? DepletionConversion;
}
