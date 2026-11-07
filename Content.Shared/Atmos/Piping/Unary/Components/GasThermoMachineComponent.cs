using Content.Shared.Atmos;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Guidebook;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Atmos.Piping.Unary.Components
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
    public sealed partial class GasThermoMachineComponent : Component
    {
        [DataField("inlet")]
        public string InletName = "pipe";

        /// <summary>
        ///     Current electrical power consumption, in watts. Increasing power increases the ability of the
        ///     thermomachine to heat or cool air.
        /// </summary>
        [DataField]
        [GuidebookData]
        public float HeatCapacity = 5000;

        /// <summary>
        ///     Palmtree: <see cref="HeatCapacity"/> with stock parts. The machine's heat capacity is
        ///     also what it draws, so better parts move more heat and cost proportionally more to run.
        /// </summary>
        [DataField]
        public float BaseHeatCapacity = 5000;

        /// <summary>
        ///     Palmtree: the machine part whose rating scales <see cref="HeatCapacity"/>.
        /// </summary>
        [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<MachinePartPrototype>))]
        public string MachinePartHeatCapacity = "MatterBin";

        [DataField, AutoNetworkedField]
        public float TargetTemperature = Atmospherics.T20C;

        /// <summary>
        ///     Tolerance for temperature setpoint hysteresis.
        /// </summary>
        [GuidebookData]
        [DataField, ViewVariables(VVAccess.ReadOnly)]
        public float TemperatureTolerance = 2f;

        /// <summary>
        ///     Implements setpoint hysteresis to prevent heater from rapidly cycling on and off at setpoint.
        ///     If true, add Sign(Cp)*TemperatureTolerance to the temperature setpoint.
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)]
        public bool HysteresisState;

        /// <summary>
        ///     Coefficient of performance. Output power / input power.
        ///     Positive for heaters, negative for freezers.
        /// </summary>
        [DataField("coefficientOfPerformance")]
        public float Cp = 0.9f; // output power / input power, positive is heat

        /// <summary>
        ///     Current minimum temperature
        ///     Ignored if heater.
        /// </summary>
        [DataField, AutoNetworkedField]
		[GuidebookData]
        public float MinTemperature = 73.15f;

        /// <summary>
        ///     Current maximum temperature
        ///     Ignored if freezer.
        /// </summary>
        [DataField, AutoNetworkedField]
		[GuidebookData]
        public float MaxTemperature = 593.15f;

        /// <summary>
        ///     Palmtree: <see cref="MinTemperature"/> with stock parts. Better capacitors widen the
        ///     range the machine can be set to, which is what the hellfire machines override.
        /// </summary>
        [DataField]
        public float BaseMinTemperature = 73.15f;

        /// <summary>
        ///     Palmtree: <see cref="MaxTemperature"/> with stock parts.
        /// </summary>
        [DataField]
        public float BaseMaxTemperature = 593.15f;

        /// <summary>
        ///     Palmtree: how many kelvin colder each capacitor rating above stock lets the target be
        ///     set to.
        /// </summary>
        [DataField]
        public float MinTemperaturePerRating = 15f;

        /// <summary>
        ///     Palmtree: how many kelvin hotter each capacitor rating above stock lets the target be
        ///     set to.
        /// </summary>
        [DataField]
        public float MaxTemperaturePerRating = 200f;

        /// <summary>
        ///     Palmtree: the machine part whose rating widens the temperature range.
        /// </summary>
        [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<MachinePartPrototype>))]
        public string MachinePartTemperature = "Capacitor";

        /// <summary>
        /// Last amount of energy added/removed from the attached pipe network
        /// </summary>
        [DataField]
        public float LastEnergyDelta;

        /// <summary>
        /// An percentage of the energy change that is leaked into the surrounding environment rather than the inlet pipe.
        /// </summary>
        [DataField]
		[GuidebookData]
       	public float EnergyLeakPercentage;

        /// <summary>
        /// If true, heat is exclusively exchanged with the local atmosphere instead of the inlet pipe air
        /// </summary>
        [DataField]
        public bool Atmospheric;
    }
}
