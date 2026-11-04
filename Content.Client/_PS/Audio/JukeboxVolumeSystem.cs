using Content.Shared._PS.CCVar;
using Content.Shared.Audio.Jukebox;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;

namespace Content.Client._PS.Audio;

/// <summary>
/// Palmtree: applies the client-side jukebox volume option to every jukebox audio stream.
/// </summary>
/// <remarks>
/// The server sends the stream's base volume in <see cref="AudioComponent.Params"/>. The audio
/// system copies that value back into <see cref="AudioComponent.Volume"/> every frame, so the
/// slider has to be applied to the params, not the live volume. Keep the original base around so
/// repeated slider changes do not stack.
/// </remarks>
public sealed class JukeboxVolumeSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private readonly Dictionary<EntityUid, float> _baseVolumes = new();
    private float _volumeOffset;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, PSCVars.JukeboxVolume, OnVolumeChanged, true);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    private void OnVolumeChanged(float value)
    {
        _volumeOffset = SharedAudioSystem.GainToVolume(value);
        ApplyVolume();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out _, out var jukebox))
        {
            if (jukebox.AudioStream is not { } stream ||
                !TryComp<AudioComponent>(stream, out var audio))
            {
                continue;
            }

            if (!_baseVolumes.TryGetValue(stream, out var baseVolume))
            {
                // Copy the struct out first: reading a field off Params directly is treated as
                // an execute access by the access analyzer.
                var parameters = audio.Params;
                baseVolume = parameters.Volume;
                _baseVolumes[stream] = baseVolume;
            }

            // SetVolume is a no-op when the value is unchanged, so this is safe to call every tick.
            _audio.SetVolume(stream, baseVolume + _volumeOffset, audio);
        }
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        _baseVolumes.Remove(args.Entity.Owner);
    }
}
