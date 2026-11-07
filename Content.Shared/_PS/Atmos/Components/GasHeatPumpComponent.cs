using Content.Shared.Atmos;
using Content.Shared.Guidebook;
using Robust.Shared.GameStates;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._PS.Atmos.Components;

/// <summary>
/// Palmtree: a two-port thermomachine that moves heat between two pipe networks instead of creating
/// or destroying it. The gas port is driven towards <see cref="TargetTemperature"/>; whatever heat is
/// taken out of it (or pushed into it) is dumped into, or taken from, the waste port.
/// Ported from /tg/station's binary thermomachine (PR #57831), which the station had from 2021 until
/// it was removed in 2022 (PR #66411).
/// </summary>
/// <remarks>
/// The vanilla <see cref="Atmos.Piping.Unary.Components.GasThermoMachineComponent"/> keeps working as
/// it always has; this is a sidegrade that needs somewhere to put the heat.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true, true), AutoGenerateComponentPause]
public sealed partial class GasHeatPumpComponent : Component
{
    /// <summary>
    /// Whether the machine is running.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled;

    /// <summary>
    /// Name of the pipe node holding the gas being conditioned.
    /// </summary>
    [DataField]
    public string GasPortName = "gas";

    /// <summary>
    /// Name of the pipe node the heat is dumped into (or taken from when heating).
    /// </summary>
    [DataField]
    public string WastePortName = "waste";

    /// <summary>
    /// Whether the machine is currently cooling. Picked automatically from the setpoint every tick,
    /// exactly like the /tg/station original: the machine is a heat pump, it does not care which way
    /// the heat goes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Cooling = true;

    /// <summary>
    /// The temperature the gas port is driven towards, in kelvin.
    /// </summary>
    [DataField, AutoNetworkedField]
    [GuidebookData]
    public float TargetTemperature = Atmospherics.T20C;

    /// <summary>
    /// Lowest temperature the target can be set to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinTemperature = 73.15f;

    /// <summary>
    /// Highest temperature the target can be set to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxTemperature = 593.15f;

    /// <summary>
    /// Thermal mass of the machine itself, in joules per kelvin. Together with the gas's own heat
    /// capacity this decides how much heat can be moved per second; it is the <c>C2</c> of
    /// /tg/station's equation.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float HeatCapacity = 5000f;

    /// <summary>
    /// The efficiency the machine cannot fall below, set by the quality of its parts. /tg/station
    /// used 0.25 for stock parts and 1.0 for tier four lasers.
    /// </summary>
    [DataField]
    public float EfficiencyFloor = 0.25f;

    /// <summary>
    /// Ceiling for <see cref="EfficiencyFloor"/>. /tg/station let good parts reach 1.0, which made
    /// the machine ignore both the waste loop temperature and an empty pipe; capping it keeps the
    /// machine demanding at every tier.
    /// </summary>
    [DataField]
    public float MaxEfficiencyFloor = 0.9f;

    /// <summary>
    /// Machine part whose rating sets <see cref="HeatCapacity"/> - the bin rating in /tg/station's
    /// formula, where <c>heat_capacity = 5000 * 2^(rating-1)</c>.
    /// </summary>
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<MachinePartPrototype>))]
    public string MachinePartHeatCapacity = "MatterBin";

    /// <summary>
    /// Machine part whose rating sets <see cref="EfficiencyFloor"/>. /tg/station used micro lasers,
    /// which this fork does not have; a manipulator is the part that decides how well a machine works.
    /// </summary>
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<MachinePartPrototype>))]
    public string MachinePartEfficiency = "Manipulator";

    /// <summary>
    /// <see cref="HeatCapacity"/> with stock parts.
    /// </summary>
    [DataField]
    public float BaseHeatCapacity = 5000f;

    /// <summary>
    /// <see cref="EfficiencyFloor"/> with stock parts.
    /// </summary>
    [DataField]
    public float BaseEfficiencyFloor = 0.25f;

    /// <summary>
    /// Hard ceiling on how much heat the machine can move, in joules per second. Without it a big
    /// temperature difference would move hundreds of kilowatts per second and take an APC with it;
    /// 50 kW is about two and a half times what a vanilla freezer can do. Better matter bins raise
    /// it, because the exchange term below is otherwise limited by the gas and the bins would not
    /// change how fast the machine can work at all.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float MaxHeatRate = 50_000f;

    /// <summary>
    /// <see cref="MaxHeatRate"/> with stock parts. Each matter bin rating doubles it, the same way
    /// each rating doubles <see cref="HeatCapacity"/>; the hard ceiling stays at 400 kW with the
    /// best bins so a single machine cannot take an APC with it.
    /// </summary>
    [DataField]
    public float BaseMaxHeatRate = 50_000f;

    /// <summary>
    /// The compressor stops adding its own waste heat once the gas is this close to the setpoint, so
    /// the machine can actually settle on it instead of always trickling heat into the loop.
    /// </summary>
    [DataField]
    public float MotorHeatDeadband = 5f;

    /// <summary>
    /// If the waste loop reaches this temperature the machine shuts itself down. With
    /// <see cref="Safeties"/> disabled it detonates instead.
    /// </summary>
    /// <remarks>
    /// /tg/station used 500 000 K, but a Space Station 14 gas mixture is clamped at
    /// <see cref="Atmospherics.Tmax"/> (262 144 K), so anything above that could never be reached.
    /// </remarks>
    [DataField]
    public float SafeTemperature = 200_000f;

    /// <summary>
    /// How far past <see cref="SafeTemperature"/> the loop has to be before the machine is allowed to
    /// give up and blow itself apart. Stops a machine that is merely at the limit from detonating.
    /// </summary>
    [DataField]
    public float DetonationMargin = 2000f;

    /// <summary>
    /// How often the detonation is rolled for, once the safeties are off. /tg/station gated this
    /// behind a warning delay; without it the roll would happen every atmos tick and the machine
    /// would be gone within a second.
    /// </summary>
    [DataField]
    public TimeSpan DetonationCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Earliest time the next detonation roll may happen.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextDetonationCheck;

    /// <summary>
    /// Whether the thermal safeties are in place. Turning them off is what lets the waste loop run
    /// away, and eventually takes the machine with it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Safeties = true;

    /// <summary>
    /// Minimum number of moles the machine needs on a side before it works at full speed.
    /// </summary>
    [DataField]
    public float MinimumMoles = 5f;

    /// <summary>
    /// Efficiency a side with almost no gas in it is reduced to.
    /// </summary>
    [DataField]
    public float EmptySideEfficiency = 0.1f;

    /// <summary>
    /// Electrical draw while running but not moving heat, in watts.
    /// </summary>
    [DataField]
    public float IdlePower = 500f;

    /// <summary>
    /// Watts of input per watt of heat moved at low load. The inverse of a coefficient of
    /// performance, so 0.25 is a COP of 4.
    /// </summary>
    [DataField]
    public float PowerPerHeat = 0.25f;

    /// <summary>
    /// Heat moved per second at which the power cost starts growing faster than the heat moved, in
    /// watts. /tg/station made the cost superlinear; this is the same idea with numbers that fit an
    /// SS14 APC. Better matter bins raise it together with <see cref="MaxHeatRate"/>, so an upgraded
    /// machine is not paying the soft cap penalty for work a bigger compressor is supposed to do.
    /// </summary>
    [DataField]
    public float PowerSoftCap = 50_000f;

    /// <summary>
    /// <see cref="PowerSoftCap"/> with stock parts.
    /// </summary>
    [DataField]
    public float BasePowerSoftCap = 50_000f;

    /// <summary>
    /// The UI key used to open this device's interface.
    /// </summary>
    [DataField]
    public GasHeatPumpUiKey UiKey = GasHeatPumpUiKey.Key;

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
    /// Temperature of the gas being conditioned, in kelvin.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float GasTemperature;

    /// <summary>
    /// Pressure of the gas being conditioned, in kPa.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float GasPressure;

    /// <summary>
    /// Temperature of the waste loop, in kelvin.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float WasteTemperature;

    /// <summary>
    /// Pressure of the waste loop, in kPa.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float WastePressure;

    /// <summary>
    /// How well the machine is working right now, from 0 to 1.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float Efficiency = 1f;

    /// <summary>
    /// Heat moved during the last atmos tick, in joules per second. Negative while cooling.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField, AutoNetworkedField]
    public float HeatMoved;

    #endregion
}

[Serializable, NetSerializable]
public enum GasHeatPumpUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class GasHeatPumpToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

[Serializable, NetSerializable]
public sealed class GasHeatPumpChangeTemperatureMessage(float temperature) : BoundUserInterfaceMessage
{
    public float Temperature { get; } = temperature;
}

[Serializable, NetSerializable]
public sealed class GasHeatPumpToggleSafetiesMessage(bool safeties) : BoundUserInterfaceMessage
{
    public bool Safeties { get; } = safeties;
}
