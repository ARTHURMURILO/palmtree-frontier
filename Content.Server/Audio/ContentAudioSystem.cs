using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.Audio;
using Content.Shared.Audio.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server.Audio;

public sealed partial class ContentAudioSystem : SharedContentAudioSystem
{
    [Dependency] private AudioSystem _serverAudio = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    private SoundCollectionPrototype? _lobbyMusicCollection = default!;
    private string[]? _lobbyPlaylist;

    // Palmtree: admin debug command - while true, round ends keep the forced playlist.
    private bool _forcedLobbyPlaylist;

    /// <summary> Palmtree: whether an admin has forced the lobby playlist (debug). </summary>
    public bool LobbyPlaylistForced => _forcedLobbyPlaylist;

    public override void Initialize()
    {
        base.Initialize();

        //changes the music collection and reshuffles the playlist to update the lobby music
        Subs.CVar(
            _cfg,
            CCVars.LobbyMusicCollection,
            x =>
            {
                //Checks to see if the sound collection exists. If it does change it if not defaults to null
                // as the new _lobbyMusicCollection meaning it wont play anything in the lobby.
                if(_prototypeManager.TryIndex<SoundCollectionPrototype>(x, out var outputSoundCollection))
                {
                    _lobbyMusicCollection = outputSoundCollection;
                }
                else
                {
                    Log.Error($"Invalid Lobby Music sound collection specified: {x}");
                    _lobbyMusicCollection = null;
                }

                _lobbyPlaylist = ShuffleLobbyPlaylist();
            },
            true);

        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnProtoReload);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        SilenceAudio();
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (obj.WasModified<AudioPresetPrototype>())
            _serverAudio.ReloadPresets();
    }

    private void OnRoundStart(RoundStartingEvent ev)
    {
        // On cleanup all entities get purged so need to ensure audio presets are still loaded
        // yeah it's whacky af.
        _serverAudio.ReloadPresets();
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (_lobbyPlaylist != null)
        {
            var session = ev.PlayerSession;
            RaiseNetworkEvent(new LobbyPlaylistChangedEvent(_lobbyPlaylist), session);
        }
    }

    private void OnRoundEnd(RoundEndMessageEvent ev)
    {
        // The lobby song is set here instead of in RestartRound,
        // because ShowRoundEndScoreboard triggers the start of the music playing
        // at the end of a round, and this needs to be set before RestartRound
        // in order for the lobby song status display to be accurate.
        if (!_forcedLobbyPlaylist) // Palmtree: keep an admin-forced playlist across rounds
            _lobbyPlaylist = ShuffleLobbyPlaylist();
        RaiseNetworkEvent(new LobbyPlaylistChangedEvent(_lobbyPlaylist ?? []));
    }

    // Palmtree: admin debug command support - force a fixed playlist until it is cleared.
    public void ForceLobbyPlaylist(string[] playlist)
    {
        _lobbyPlaylist = playlist;
        _forcedLobbyPlaylist = true;
        SendLobbyPlaylistToLobby();
    }

    // Palmtree: admin debug command support - clear the force and shuffle the configured collection.
    public void ClearForcedLobbyPlaylist()
    {
        _forcedLobbyPlaylist = false;
        _lobbyPlaylist = ShuffleLobbyPlaylist();
        SendLobbyPlaylistToLobby();
    }

    // Palmtree: only tell clients waiting in the lobby - sending the playlist to in-game clients
    // would start lobby music mid-round.
    private void SendLobbyPlaylistToLobby()
    {
        var ticker = EntityManager.System<GameTicker>();
        var playlist = _lobbyPlaylist ?? [];

        foreach (var session in _playerManager.Sessions)
        {
            // NotReadyToPlay / ReadyToPlay are both pre-round lobby states; JoinedGame is in-round.
            if (!ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) ||
                status == PlayerGameStatus.JoinedGame)
            {
                continue;
            }

            RaiseNetworkEvent(new LobbyPlaylistChangedEvent(playlist), session);
        }
    }

    private string[] ShuffleLobbyPlaylist()
    {
        if (_lobbyMusicCollection == null)
        {
            return [];
        }

        var playlist = _lobbyMusicCollection.PickFiles
                                            .Select(x => x.ToString())
                                            .ToArray();
        _robustRandom.Shuffle(playlist);

        return playlist;
    }
}
