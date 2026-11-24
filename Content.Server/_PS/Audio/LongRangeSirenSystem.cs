using Content.Shared.Audio;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._PS.Audio;

/// <summary>
/// Palmtree: drives <see cref="LongRangeSirenComponent"/> by playing a looping, PVS-overridden stream.
/// </summary>
public sealed class LongRangeSirenSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LongRangeSirenComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var query = EntityQueryEnumerator<LongRangeSirenComponent>();
        while (query.MoveNext(out var uid, out var siren))
        {
            UpdateSiren(uid, siren);
        }
    }

    private void UpdateSiren(EntityUid uid, LongRangeSirenComponent siren)
    {
        // The ambient component is the on/off switch and sound source. PoweredLight toggles it when
        // the fixture's bulb/power state changes.
        if (!TryComp<AmbientSoundComponent>(uid, out var ambient) || !ambient.Enabled)
        {
            StopStream(siren);
            return;
        }

        if (siren.Stream is { } existing && !TerminatingOrDeleted(existing))
            return;

        var stream = _audio.PlayPvs(ambient.Sound, uid, AudioParams.Default
            .WithLoop(true)
            .WithVolume(ambient.Volume)
            .WithReferenceDistance(siren.ReferenceDistance)
            .WithMaxDistance(siren.MaxDistance));

        if (stream is not { } playing)
            return;

        // A siren should carry through walls, not be muffled by them.
        playing.Component.Flags |= AudioFlags.NoOcclusion;
        _pvs.AddGlobalOverride(playing.Entity);
        siren.Stream = playing.Entity;
    }

    private void StopStream(LongRangeSirenComponent siren)
    {
        if (siren.Stream is not { } stream)
            return;

        _pvs.RemoveGlobalOverride(stream);
        _audio.Stop(stream);
        siren.Stream = null;
    }

    private void OnShutdown(EntityUid uid, LongRangeSirenComponent siren, ComponentShutdown args)
    {
        StopStream(siren);
    }
}
