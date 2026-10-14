using Robust.Shared.Serialization;

namespace Content.Shared._PS.Audio;

/// <summary>
/// Palmtree: asks a jukebox to stop the song that is currently playing and advance to the next
/// track in the queue (or stop when the queue is empty).
/// </summary>
[Serializable, NetSerializable]
public sealed class JukeboxSkipMessage : BoundUserInterfaceMessage;
