using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.Construction.Prototypes;
using Content.Shared._PS.Consent; // Palmtree
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Preferences.Managers
{
    public interface IServerPreferencesManager
    {
        void Init();

        Task LoadData(ICommonSession session, CancellationToken cancel);
        void FinishLoad(ICommonSession session);
        void OnClientDisconnected(ICommonSession session);

        bool TryGetCachedPreferences(NetUserId userId, [NotNullWhen(true)] out PlayerPreferences? playerPreferences);
        PlayerPreferences GetPreferences(NetUserId userId);
        PlayerPreferences? GetPreferencesOrNull(NetUserId? userId);
        IEnumerable<KeyValuePair<NetUserId, ICharacterProfile>> GetSelectedProfilesForPlayers(List<NetUserId> userIds);
        bool HavePreferencesLoaded(ICommonSession session);
        Task RefreshPreferencesAsync(ICommonSession session, CancellationToken cancel); // Frontier
        Task SetProfile(NetUserId userId, int slot, ICharacterProfile profile, bool validateFields = true); // Frontier: add validateFields
        Task SetConstructionFavorites(NetUserId userId, List<ProtoId<ConstructionPrototype>> favorites);

        /// <summary>
        /// Palmtree: sets the player's NSFW age gate answer (the only writer after the one-time
        /// prompt is the nsfwtoggle console command).
        /// </summary>
        /// <returns>False if the player's preferences have not loaded yet (nothing was changed).</returns>
        Task<bool> SetNsfwConsentAsync(NetUserId userId, NsfwConsentState state);
    }
}
