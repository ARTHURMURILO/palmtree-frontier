using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: sets one of the ship's power pools (the console redistributes the difference).
/// </summary>
[Serializable, NetSerializable]
public sealed class ShieldingConsoleSetPoolMessage(ShipPowerPool pool, int value) : BoundUserInterfaceMessage
{
    public ShipPowerPool Pool { get; } = pool;
    public int Value { get; } = value;
}

/// <summary>
/// Palmtree: pins a power pool so adjusting the others does not move it.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShieldingConsoleSetPoolLockMessage(ShipPowerPool pool, bool locked) : BoundUserInterfaceMessage
{
    public ShipPowerPool Pool { get; } = pool;
    public bool Locked { get; } = locked;
}

/// <summary>
/// Palmtree: applies one of the Attack/Defense/Balanced presets.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShieldingConsoleSetPresetMessage(ShipPowerPreset preset) : BoundUserInterfaceMessage
{
    public ShipPowerPreset Preset { get; } = preset;
}

/// <summary>
/// Palmtree: silences (or unsilences) the console.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShieldingConsoleSetSilentMessage(bool silent) : BoundUserInterfaceMessage
{
    public bool Silent { get; } = silent;
}

/// <summary>
/// Palmtree: instantly redistributes shield energy into one facing, or evenly with
/// <see cref="ShieldFacing.All"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShieldingConsoleRedistributeMessage(ShieldFacing facing) : BoundUserInterfaceMessage
{
    public ShieldFacing Facing { get; } = facing;
}
