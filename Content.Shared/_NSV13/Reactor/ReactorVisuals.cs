// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._NSV13.Reactor;

/// <summary>
///     Appearance keys shared by the two ported reactors.
/// </summary>
/// <remarks>
///     The DM originals drove their sprites from <c>update_icon()</c>, which was called from inside
///     the physics tick - the state was a direct readout of the core temperature. SS14 wants that
///     pushed through Appearance so the client can interpolate/animate it, so the same set of states
///     is raised as appearance data and mapped onto the ported RSI by a GenericVisualizer.
/// </remarks>
[Serializable, NetSerializable]
public enum RbmkVisuals
{
    /// <summary>Sprite layer state for the core body.</summary>
    Core,
}

[Serializable, NetSerializable]
public enum RbmkVisualState
{
    Off,
    On,
    Hot,
    VeryHot,
    Overheat,
    Meltdown,
    Slagged,
}

[Serializable, NetSerializable]
public enum StormdriveVisuals
{
    /// <summary>Sprite layer state for the core body.</summary>
    Core,

    /// <summary>Rod overlay: <c>rods_{count}_{positionBand}</c>, or hidden when there are no rods.</summary>
    Rods,
}

/// <summary>
///     Stormdrive core appearance states, one per DM <c>icon_state</c>.
/// </summary>
[Serializable, NetSerializable]
public enum StormdriveVisualState
{
    Off,
    Starting,
    Maintenance,
    On,
    Hot,
    Overheat,
    Meltdown,
    Broken,
    Repair,
    Reinforce,
    Refit,
}
