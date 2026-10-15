using System.Linq;
using Content.Client._PS.Consent; // Palmtree
using Content.Client.Administration.Managers;
using Content.Shared._PS.Consent; // Palmtree
using Content.Shared.Construction.Prototypes;
using Content.Shared.Preferences;
using Robust.Client;
using Robust.Client.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby
{
    /// <summary>
    ///     Receives <see cref="PlayerPreferences" /> and <see cref="GameSettings" /> from the server during the initial
    ///     connection.
    ///     Stores preferences on the server through <see cref="SelectCharacter" /> and <see cref="UpdateCharacter" />.
    /// </summary>
    public sealed class ClientPreferencesManager : IClientPreferencesManager
    {
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IClientAdminManager _adminManager = default!; // Palmtree

        public event Action? OnServerDataLoaded;

        public GameSettings Settings { get; private set; } = default!;
        public PlayerPreferences Preferences { get; private set; } = default!;

        public void Initialize()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>(HandlePreferencesAndSettings);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>();
            _netManager.RegisterNetMessage<MsgSelectCharacter>();
            _netManager.RegisterNetMessage<MsgDeleteCharacter>();

            _baseClient.RunLevelChanged += BaseClientOnRunLevelChanged;
        }

        private void BaseClientOnRunLevelChanged(object? sender, RunLevelChangedEventArgs e)
        {
            if (e.NewLevel == ClientRunLevel.Initialize)
            {
                Settings = default!;
                Preferences = default!;
                NsfwGate.SetState(NsfwConsentState.Unanswered); // Palmtree
            }
        }

        public void SelectCharacter(ICharacterProfile profile)
        {
            SelectCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void SelectCharacter(int slot)
        {
            Preferences = new PlayerPreferences(Preferences.Characters, slot, Preferences.AdminOOCColor, Preferences.ConstructionFavorites, Preferences.NsfwConsent);
            var msg = new MsgSelectCharacter
            {
                SelectedCharacterIndex = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void UpdateCharacter(ICharacterProfile profile, int slot)
        {
            var collection = IoCManager.Instance!;
            profile.EnsureValid(_playerManager.LocalSession!, collection, _adminManager.IsActive()); // Palmtree: admins ignore playtime requirements
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters) {[slot] = profile};
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.ConstructionFavorites, Preferences.NsfwConsent);
            var msg = new MsgUpdateCharacter
            {
                Profile = profile,
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void CreateCharacter(ICharacterProfile profile)
        {
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters);
            var lowest = Enumerable.Range(0, Settings.MaxCharacterSlots)
                .Except(characters.Keys)
                .FirstOrNull();

            if (lowest == null)
            {
                throw new InvalidOperationException("Out of character slots!");
            }

            var l = lowest.Value;
            characters.Add(l, profile);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.ConstructionFavorites, Preferences.NsfwConsent);

            UpdateCharacter(profile, l);
        }

        public void DeleteCharacter(ICharacterProfile profile)
        {
            DeleteCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void DeleteCharacter(int slot)
        {
            var characters = Preferences.Characters.Where(p => p.Key != slot);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.ConstructionFavorites, Preferences.NsfwConsent);
            var msg = new MsgDeleteCharacter
            {
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void UpdateConstructionFavorites(List<ProtoId<ConstructionPrototype>> favorites)
        {
            Preferences = new PlayerPreferences(Preferences.Characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, favorites, Preferences.NsfwConsent);
            var msg = new MsgUpdateConstructionFavorites
            {
                Favorites = favorites
            };
            _netManager.ClientSendMessage(msg);
        }

        /// <summary>
        /// Palmtree: answers the one-time NSFW consent prompt. The server rejects this if the
        /// account already answered; afterwards only the nsfwtoggle console command can change it.
        /// The local gate updates immediately so the prompt closes and the UI unlocks.
        /// </summary>
        public void UpdateNsfwConsent(bool allowed)
        {
            var state = allowed ? NsfwConsentState.Allowed : NsfwConsentState.Denied;
            Preferences = new PlayerPreferences(Preferences.Characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.ConstructionFavorites, state);
            NsfwGate.SetState(state);

            var msg = new MsgUpdateNsfwConsent
            {
                Allowed = allowed
            };
            _netManager.ClientSendMessage(msg);
        }

        private void HandlePreferencesAndSettings(MsgPreferencesAndSettings message)
        {
            Preferences = message.Preferences;
            Settings = message.Settings;
            NsfwGate.SetState(message.Preferences.NsfwConsent); // Palmtree

            OnServerDataLoaded?.Invoke();
        }
    }
}
