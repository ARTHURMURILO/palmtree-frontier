// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     A stormdrive control rod, the physical item version.
/// </summary>
/// <remarks>
///     Ported from <c>control_rods.dm</c>. Five rod grades exist: standard (100 % integrity, ×1
///     effectiveness), techfab (80 %, ×0.8), crystalline (200 %, ×1), plasma-sheathed (100 %, ×-0.5
///     - it <em>adds</em> reactivity) and depleted (0 %, ×0, radioactive).
///     The reactor averages both fields across its mounted rods, which is exactly what
///     <c>handle_control_rod_efficiency()</c> and <c>handle_control_rod_integrity()</c> did.
/// </remarks>
[RegisterComponent]
public sealed partial class ControlRodComponent : Component
{
    /// <summary>Current rod integrity. Wear is subtracted from this while the core runs.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Integrity = 100f;

    /// <summary>Integrity the rod started with, for the percentage readout.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float MaxIntegrity = 100f;

    /// <summary>
    ///     Multiplier on how much moderating authority this rod contributes. Negative values make
    ///     the rod a reactivity source rather than a sink.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public float Effectiveness = 1f;

    /// <summary>Depleted rods never cool the core and are swapped out by the tick.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Spent => Integrity <= 0f;

    /// <summary>
    ///     Whether the core may swap this rod for an irradiated one once it burns out. False on the
    ///     irradiated rod itself: it is the replacement, and the DM original explicitly skipped
    ///     <c>/obj/item/control_rod/irradiated</c> when looking for rods to replace. Without this
    ///     flag a burnt-out rod would be respawned every single tick.
    /// </summary>
    [DataField]
    public bool Replaceable = true;
}
