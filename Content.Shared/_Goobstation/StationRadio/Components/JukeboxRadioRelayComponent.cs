using Robust.Shared.GameStates;

namespace Content.Shared._Goobstation.StationRadio.Components;

/// <summary>
/// Marks a machine that mirrors its jukebox playback onto the station radio speakers when it is
/// device-linked into a radio rig (and that rig is linked into a radio server).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class JukeboxRadioRelayComponent : Component;
