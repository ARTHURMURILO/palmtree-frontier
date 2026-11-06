using Robust.Shared.Serialization;

namespace Content.Shared._PS.Atmos;

/// <summary>
/// Appearance keys shared by the Palmtree atmospherics devices ported from /tg/station.
/// </summary>
[Serializable, NetSerializable]
public enum TemperatureGateVisuals : byte
{
    State,
}

/// <summary>
/// The three sprite states of a temperature gate.
/// </summary>
[Serializable, NetSerializable]
public enum TemperatureGateState : byte
{
    /// <summary>
    /// The gate is disabled, unpowered or its condition is not met.
    /// </summary>
    Off,

    /// <summary>
    /// The gate is enabled and its condition is met, but there is nothing to transfer.
    /// </summary>
    On,

    /// <summary>
    /// Gas is currently flowing through the gate.
    /// </summary>
    Flowing,
}

[Serializable, NetSerializable]
public enum GasMeterVisuals : byte
{
    /// <summary>
    /// Which pressure state the dial shows. See <see cref="GasMeterPressure"/>.
    /// </summary>
    Pressure,

    /// <summary>
    /// Which colour the dial is tinted with. See <see cref="GasMeterTemperature"/>.
    /// </summary>
    Temperature,
}

/// <summary>
/// Coarse pressure bands used by the atmos meter dial.
/// </summary>
[Serializable, NetSerializable]
public enum GasMeterPressureLevel : byte
{
    Zero,
    Low,
    Medium,
    High,
    Extreme,
}

/// <summary>
/// Coarse temperature bands used to tint the atmos meter dial.
/// </summary>
[Serializable, NetSerializable]
public enum GasMeterTemperatureLevel : byte
{
    Freezing,
    Cold,
    Normal,
    Warm,
    Hot,
    Burning,
}
