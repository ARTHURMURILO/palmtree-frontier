using Content.Shared._Goobstation.StationRadio.Components;
using Content.Shared._Goobstation.StationRadio.Events;
using Content.Shared._PS.Audio;
using Content.Shared.DeviceLinking;

namespace Content.Server._Goobstation.StationRadio;

/// <summary>
/// Mirrors a jukebox's playback onto every station radio speaker, as long as the machine is
/// wired into a radio rig and that rig is wired into a radio server.
/// Ported from Goob Station's vinyl player relay (<c>VinylPlayerSystem</c>), now driven by the
/// jukebox playback events instead of an item slot.
/// </summary>
public sealed class JukeboxRadioRelaySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JukeboxRadioRelayComponent, JukeboxSongStartedEvent>(OnSongStarted);
        SubscribeLocalEvent<JukeboxRadioRelayComponent, JukeboxSongStoppedEvent>(OnSongStopped);
    }

    private void OnSongStarted(EntityUid uid, JukeboxRadioRelayComponent comp, JukeboxSongStartedEvent args)
    {
        if (!CheckForRadioRig(uid))
            return;

        var query = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (query.MoveNext(out var receiver, out var receiverComp))
        {
            // A speaker that is already playing something keeps its stream; it will pick up the
            // next song once it stops.
            if (receiverComp.SoundEntity.HasValue)
                continue;

            RaiseLocalEvent(receiver, new StationRadioMediaPlayedEvent(args.Song));
        }
    }

    private void OnSongStopped(EntityUid uid, JukeboxRadioRelayComponent comp, JukeboxSongStoppedEvent args)
    {
        if (!CheckForRadioRig(uid))
            return;

        var query = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (query.MoveNext(out var receiver, out _))
        {
            RaiseLocalEvent(receiver, new StationRadioMediaStoppedEvent());
        }
    }

    private bool CheckForRadioRig(EntityUid uid)
    {
        if (!TryComp<DeviceLinkSourceComponent>(uid, out var source))
            return false;

        foreach (var linked in source.LinkedPorts.Keys)
        {
            if (HasComp<RadioRigComponent>(linked) && CheckForRadioServer(linked))
                return true;
        }

        return false;
    }

    private bool CheckForRadioServer(EntityUid uid)
    {
        if (!TryComp<DeviceLinkSinkComponent>(uid, out var sink))
            return false;

        foreach (var linked in sink.LinkedSources)
        {
            if (HasComp<StationRadioServerComponent>(linked))
                return true;
        }

        return false;
    }
}
