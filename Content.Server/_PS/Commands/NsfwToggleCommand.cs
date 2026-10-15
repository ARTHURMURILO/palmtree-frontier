using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Preferences.Managers;
using Content.Shared._PS.Consent;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._PS.Commands;

/// <summary>
/// Palmtree: sets a player's NSFW age gate. This is the only way to change the flag after the
/// player answered the one-time lobby prompt (the prompt itself only accepts an unanswered
/// account).
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class NsfwToggleCommand : IConsoleCommand
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;

    public string Command => "nsfwtoggle";
    public string Description => "Sets a player's NSFW consent flag (true = enabled, false = disabled).";
    public string Help => $"Usage: {Command} <user> <true/false>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("nsfwtoggle-command-usage", ("command", Command)));
            return;
        }

        if (!bool.TryParse(args[1], out var allowed))
        {
            shell.WriteError(Loc.GetString("nsfwtoggle-command-invalid-value", ("value", args[1])));
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError(Loc.GetString("nsfwtoggle-command-no-player", ("user", args[0])));
            return;
        }

        var state = allowed ? NsfwConsentState.Allowed : NsfwConsentState.Denied;
        if (!await _preferences.SetNsfwConsentAsync(session.UserId, state))
        {
            shell.WriteError(Loc.GetString("nsfwtoggle-command-not-loaded", ("user", session.Name)));
            return;
        }

        _adminLogger.Add(LogType.AdminMessage, LogImpact.Medium,
            $"{shell.Player?.Name ?? "Console"} set the NSFW consent of {session.Name} to {allowed}");

        shell.WriteLine(Loc.GetString("nsfwtoggle-command-success",
            ("user", session.Name),
            ("state", Loc.GetString(allowed ? "nsfwtoggle-command-enabled" : "nsfwtoggle-command-disabled"))));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager),
                Loc.GetString("nsfwtoggle-command-completion-user")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.Booleans,
                Loc.GetString("nsfwtoggle-command-completion-state")),
            _ => CompletionResult.Empty,
        };
    }
}
