using Content.Shared.Guidebook;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._PS.Atmos.Components;

/// <summary>
/// Palmtree: a binary atmos device that moves heat - but not gas - from its inlet pipeline to its
/// outlet pipeline. The inlet side cools down and the outlet side heats up.
/// Ported from /tg/station's <c>/obj/machinery/atmospherics/components/binary/temperature_pump</c>.
/// </summary>
/// <remarks>
/// The device only moves heat in one direction: from the hotter pipeline to the colder one. It
/// never works in reverse, exactly like the /tg/station original.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true, true), AutoGenerateComponentPause]
public sealed partial class GasTemperaturePumpComponent : Component
{
    /// <summary>
    /// Whether the pump is running.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled;

    /// <summary>
    /// Name of the pipe node that gets cooled.
    /// </summary>
    [DataField]
    public string InletName = "inlet";

    /// <summary>
    /// Name of the pipe node that gets heated.
    /// </summary>
    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    /// Percentage of the temperature difference the pump moves per second, from 0 to 100.
    /// </summary>
    [DataField, AutoNetworkedField]
    [GuidebookData]
    public float HeatTransferRate;

    /// <summary>
    /// Highest value <see cref="HeatTransferRate"/> can be set to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxHeatTransferRate = 100f;

    /// <summary>
    /// The UI key used to open this device's interface.
    /// </summary>
    [DataField]
    public GasTemperaturePumpUiKey UiKey = GasTemperaturePumpUiKey.Key;

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
    /// Heat moved during the last atmos tick, in joules per second.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float TransferredHeat;

    #endregion
}

[Serializable, NetSerializable]
public enum GasTemperaturePumpUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class GasTemperaturePumpToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

[Serializable, NetSerializable]
public sealed class GasTemperaturePumpChangeRateMessage(float rate) : BoundUserInterfaceMessage
{
    public float Rate { get; } = rate;
}
