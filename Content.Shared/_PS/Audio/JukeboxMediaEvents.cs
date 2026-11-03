using Robust.Shared.Audio;
using Robust.Shared.GameObjects;

namespace Content.Shared._PS.Audio;

/// <summary>
/// Raised on a jukebox when it starts (or resumes) playing a song.
/// Palmtree: lets other machines mirror the music, e.g. the station radio relay.
/// </summary>
public sealed class JukeboxSongStartedEvent(SoundSpecifier song) : EntityEventArgs
{
    public readonly SoundSpecifier Song = song;
}

/// <summary>
/// Raised on a jukebox when its playback stops or pauses.
/// </summary>
public sealed class JukeboxSongStoppedEvent : EntityEventArgs;
