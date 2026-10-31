// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor.Components;

/// <summary>
///     One of the three anomalies an unstable stormdrive can tear into realspace.
/// </summary>
/// <remarks>
///     Ported from <c>stormdrive.dm</c>'s <c>/obj/effect/anomaly/stormdrive/*</c> subtypes. The DM
///     originals were their own anomaly datum with a custom <c>anomalyEffect()</c>; here they are a
///     plain component because their behaviour is nothing like the generic SS14 anomaly machine.
/// </remarks>
[RegisterComponent]
public sealed partial class StormdriveAnomalyComponent : Component
{
    [DataField]
    public StormdriveAnomalyKind Kind = StormdriveAnomalyKind.Surge;

    /// <summary>Seconds between pulses, matching the DM anomaly tick.</summary>
    [DataField]
    public TimeSpan PulseInterval = TimeSpan.FromSeconds(1.5);

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextPulse;

    /// <summary>Total lifetime before it detonates.</summary>
    [DataField]
    public TimeSpan Lifespan = TimeSpan.FromSeconds(300);

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan SpawnedAt;

    /// <summary>Peak radiation the sheer anomaly can emit, in rads/s.</summary>
    [DataField]
    public float SheerRadiation = 25f;

    /// <summary>EMP range for the surge anomaly.</summary>
    [DataField]
    public float SurgeEmpRange = 3f;

    /// <summary>Tile temperature the anomaly leaves behind when it detonates (DM: 5000 K).</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float DetonationTemperature = 5000f;

    /// <summary>Range the squall anomaly hurls things around in.</summary>
    [DataField]
    public float SquallRange = 4f;
}

public enum StormdriveAnomalyKind : byte
{
    /// <summary>EMP and electrical shocks.</summary>
    Surge,

    /// <summary>Radiation and nucleium fallout.</summary>
    Sheer,

    /// <summary>Gravity shear: throws everything around.</summary>
    Squall,
}
