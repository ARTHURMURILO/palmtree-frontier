using Content.Server.Preferences.Managers; // Palmtree
using Content.Shared._PS.Consent; // Palmtree
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.GameTicking.Commands;

[AnyCommand]
public sealed class ToggleReadyCommand : LocalizedEntityCommands
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!; // Palmtree

    public override string Command => "toggleready";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (_gameTicker.RunLevel != GameRunLevel.PreRoundLobby)
        {
            shell.WriteError(Loc.GetString("shell-can-only-run-from-pre-round-lobby"));
            return;
        }

        if (!bool.TryParse(args[0], out var ready))
        {
            shell.WriteError(Loc.GetString("shell-argument-must-be-boolean"));
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

        _gameTicker.ToggleReady(player, ready);
    }
}
