// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Plays an engine's alarms for the crew of the ship the engine is bolted to, and nobody else.
/// </summary>
/// <remarks>
///     <c>PlayPvs</c> broadcasts to everyone inside the audio range. On a ship-based fork that means
///     a neighbouring hull, or a crewman on EVA just outside the engine room, hears another ship's
///     meltdown klaxon as if it were their own. This builds the recipient list from sessions whose
///     attached entity is standing on the same grid, and scales the falloff distance to the grid's
///     own diagonal so a big ship's far end still hears it while the sound dies at the hull.
/// </remarks>
public sealed class EngineAudioSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    /// <summary>
    ///     Play <paramref name="sound"/> at <paramref name="source"/>, but only for players standing
    ///     on the same grid as the source.
    /// </summary>
    /// <param name="loop">Whether the sound should loop until <see cref="SharedAudioSystem.Stop"/>.</param>
    /// <returns>The audio entity, or null when nobody was around to hear it.</returns>
    public EntityUid? PlayOnGrid(SoundSpecifier? sound, EntityUid source, bool loop = false, AudioParams? audioParams = null)
    {
        if (sound == null)
            return null;

        var xform = Transform(source);
        if (xform.GridUid is not { } grid)
        {
            // Not on a grid at all (map entity, admin spawn): ordinary positional behaviour is fine.
            return _audio.PlayPvs(sound, source, audioParams)?.Entity;
        }

        var filter = Filter.Empty();
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } player)
                continue;

            if (Transform(player).GridUid != grid)
                continue;

            filter.AddPlayer(session);
        }

        if (filter.Count == 0)
            return null;

        // The ship's own diagonal: long enough to reach the far end of a big hull, short enough that
        // the volume falls away naturally instead of sounding like it is right next to you.
        var range = 10f;
        if (TryComp<MapGridComponent>(grid, out var gridComp))
            range = MathF.Max(gridComp.LocalAABB.Size.Length(), range);

        var parameters = (audioParams ?? AudioParams.Default).WithMaxDistance(range);
        if (loop)
            parameters = parameters.WithLoop(true);

        return _audio.PlayEntity(sound, filter, source, false, parameters)?.Entity;
    }
}
