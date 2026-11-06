using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._PS.Atmos.Components;

/// <summary>
/// Palmtree: a dial gauge mounted on an atmos pipe that shows the pressure (by bar length) and the
/// temperature (by colour) of the gas flowing through it.
/// Ported from /tg/station's <c>/obj/machinery/meter</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class GasMeterComponent : Component
{
    /// <summary>
    /// Name of the pipe node whose pipeline is being measured.
    /// </summary>
    [DataField]
    public string NodeName = "monitored";

    /// <summary>
    /// The server time at which the next UI/examine update will be sent.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    /// <summary>
    /// How often the networked readings are refreshed.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Pressure of the measured pipeline, in kPa.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float Pressure;

    /// <summary>
    /// Temperature of the measured pipeline, in kelvin.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float Temperature;

    /// <summary>
    /// The pressure band currently shown on the dial.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public GasMeterPressureLevel PressureLevel = GasMeterPressureLevel.Zero;

    /// <summary>
    /// The temperature band the dial is currently tinted with.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public GasMeterTemperatureLevel TemperatureLevel = GasMeterTemperatureLevel.Normal;
}
