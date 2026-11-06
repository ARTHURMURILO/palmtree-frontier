using Content.Shared.Atmos;
using Content.Shared.Guidebook;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._PS.Atmos.Components;

/// <summary>
/// Palmtree: a binary atmos device that only lets gas through when the temperature of the gas on its
/// inlet side is below (or, when inverted, above) a configurable threshold.
/// Ported from /tg/station's <c>/obj/machinery/atmospherics/components/binary/temperature_gate</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true, true), AutoGenerateComponentPause]
public sealed partial class GasTemperatureGateComponent : Component
{
    /// <summary>
    /// Whether the gate is open (true) or shut (false). The gate still has to satisfy its temperature
    /// condition and be powered before gas is actually transferred.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled;

    /// <summary>
    /// Name of the pipe node the gate compares against <see cref="TargetTemperature"/>.
    /// </summary>
    [DataField]
    public string InletName = "inlet";

    /// <summary>
    /// Name of the pipe node gas is released into.
    /// </summary>
    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    /// The temperature the inlet gas is compared against, in kelvin.
    /// </summary>
    [DataField, AutoNetworkedField]
    [GuidebookData]
    public float TargetTemperature = Atmospherics.T0C;

    /// <summary>
    /// Lowest temperature the target can be set to.
    /// </summary>
    [DataField]
    public float MinTemperature = Atmospherics.TCMB;

    /// <summary>
    /// Highest temperature the target can be set to.
    /// </summary>
    [DataField]
    public float MaxTemperature = 4500f;

    /// <summary>
    /// When false the gate lets gas through while the inlet is colder than the target.
    /// When true it lets gas through while the inlet is hotter than the target.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Inverted;

    /// <summary>
    /// Maximum amount of gas the gate can move, in litres per second.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float MaxTransferRate = Atmospherics.MaxTransferRate;

    /// <summary>
    /// The UI key used to open this device's interface.
    /// </summary>
    [DataField]
    public GasTemperatureGateUiKey UiKey = GasTemperatureGateUiKey.Key;

    /// <summary>
    /// The server time at which the next UI update will be sent.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextUiUpdate = TimeSpan.Zero;

    /// <summary>
    /// How often the UI is dirtied with fresh sensor readings.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    #region UI/Examine Info

    /// <summary>
    /// Temperature of the gas on the inlet side, in kelvin.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float InletTemperature;

    /// <summary>
    /// Temperature of the gas on the outlet side, in kelvin.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float OutletTemperature;

    /// <summary>
    /// How much gas the gate moved during the last atmos tick, in litres per second.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float FlowRate;

    #endregion
}

[Serializable, NetSerializable]
public enum GasTemperatureGateUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class GasTemperatureGateToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

[Serializable, NetSerializable]
public sealed class GasTemperatureGateChangeTemperatureMessage(float temperature) : BoundUserInterfaceMessage
{
    public float Temperature { get; } = temperature;
}

[Serializable, NetSerializable]
public sealed class GasTemperatureGateInvertMessage(bool inverted) : BoundUserInterfaceMessage
{
    public bool Inverted { get; } = inverted;
}
