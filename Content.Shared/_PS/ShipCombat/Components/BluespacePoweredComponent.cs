namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a device powered remotely by the ship's Bluespace Power Relay - no wires. The ship
/// power system sets <see cref="Performance"/> and toggles the device's ApcPowerReceiver every tick.
/// </summary>
[RegisterComponent]
public sealed partial class BluespacePoweredComponent : Component
{
    /// <summary>Which power pool feeds this device.</summary>
    [DataField]
    public ShipPowerPool Pool = ShipPowerPool.Thrusters;

    /// <summary>Power draw at 100% performance, in watts. Scaled by the pool performance.</summary>
    [DataField]
    public float RatedPower;

    /// <summary>Performance multiplier from the last power tick (0 = unpowered/offline).</summary>
    public float Performance = 1f;

    /// <summary>True when the relay powered this device on the last tick.</summary>
    public bool Powered = true;
}
