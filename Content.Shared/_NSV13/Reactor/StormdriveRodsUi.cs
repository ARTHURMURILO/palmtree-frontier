// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._NSV13.Reactor;

/// <summary>
///     UI key for the stormdrive's own rod bay.
/// </summary>
/// <remarks>
///     The DM reactor had its own TGUI (<c>ui_data</c> returned <c>mounted_control_rods</c>) separate
///     from the control console: you walked up to the machine to pull a rod. That is the window this
///     key opens.
/// </remarks>
[Serializable, NetSerializable]
public enum StormdriveRodsUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class StormdriveRodsBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>Currently installed rods, in installation order.</summary>
    public List<StormdriveRodEntry> Rods = new();

    /// <summary>Maximum the core accepts.</summary>
    public int Capacity = 5;

    /// <summary>Reactor is in maintenance mode, so the lid is open.</summary>
    public bool Maintenance;

    /// <summary>The reactor has melted down; nothing can be pulled until it is cleaned out.</summary>
    public bool Broken;

    /// <summary>Short-lived notice (a refusal, mostly).</summary>
    public string Notice = string.Empty;
}

[Serializable, NetSerializable]
public sealed class StormdriveRodEntry
{
    public string Name = string.Empty;

    /// <summary>Integrity as a percentage of the rod's own maximum.</summary>
    public float Percent;

    /// <summary>Effectiveness multiplier; negative rods feed the reaction.</summary>
    public float Effectiveness;

    public bool Spent;
}

/// <summary>Pull the rod at <see cref="Index"/>. Refused unless the reactor is in maintenance.</summary>
[Serializable, NetSerializable]
public sealed class StormdriveRemoveRodMessage : BoundUserInterfaceMessage
{
    public int Index;

    public StormdriveRemoveRodMessage(int index)
    {
        Index = index;
    }
}
