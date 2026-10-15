using Content.Server.Preferences.Managers; // Palmtree
using Content.Shared._PS.Consent; // Palmtree
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.GameTicking.Commands
{
    [AnyCommand]
    sealed class ToggleReadyCommand : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _e = default!;
        [Dependency] private readonly IServerPreferencesManager _preferences = default!; // Palmtree

        public string Command => "toggleready";
        public string Description => "";
        public string Help => "";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var player = shell.Player;
            if (args.Length != 1)
            {
                shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
                return;
            }
            if (player == null)
            {
                return;
            }

            // Palmtree: the lobby consent prompt must be answered before the player can ready up
            // and join. Declining NSFW content does not block joining.
            if (_preferences.TryGetCachedPreferences(player.UserId, out var prefs) &&
                prefs.NsfwConsent == NsfwConsentState.Unanswered)
            {
                shell.WriteError(Loc.GetString("nsfw-consent-must-answer"));
                return;
            }

            var ticker = _e.System<GameTicker>();
            ticker.ToggleReady(player, bool.Parse(args[0]));
        }
    }
}
