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
    /// The gate is enabled and powered. This is also what /tg/station shows while the temperature
    /// condition is not met, so the sprite does not flicker between open and closed.
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
    /// Which pressure state the dial shows. See <see cref="GasMeterPressureLevel"/>.
    /// </summary>
    Pressure,

    /// <summary>
    /// Which colour the dial is tinted with. See <see cref="GasMeterTemperatureLevel"/>.
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

/// <summary>
/// Sprite layers of the atmos meter that have to stay visible above a floor tile. Kept in the _PS
/// namespace so the shared <c>SubFloorHide</c> layer enum does not need a fork-only value.
/// </summary>
[Serializable, NetSerializable]
public enum GasMeterVisualLayers : byte
{
    /// <summary>
    /// The gauge bar with the pressure reading, tinted by the gas temperature.
    /// </summary>
    Dial,
}

/// <summary>
/// Appearance keys of the heat pump. The body art is the vanilla freezer/heater art, so the only
/// thing that changes is which of the four combinations it is showing.
/// </summary>
[Serializable, NetSerializable]
public enum GasHeatPumpVisuals : byte
{
    /// <summary>
    /// Which body state the machine is in. See <see cref="GasHeatPumpVisualState"/>.
    /// </summary>
    State,
}

/// <summary>
/// Body states of the heat pump: the freezer art while cooling, the heater art while heating, and the
/// running variant of each while the machine is on.
/// </summary>
[Serializable, NetSerializable]
public enum GasHeatPumpVisualState : byte
{
    OffCooling,
    OnCooling,
    OffHeating,
    OnHeating,
}
