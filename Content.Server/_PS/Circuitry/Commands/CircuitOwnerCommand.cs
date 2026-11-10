using Content.Server._PS.Circuitry.Components;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._PS.Circuitry.Commands;

/// <summary>
/// Palmtree: administrative ownership management for circuit assemblies. Use "clear" to release
/// an assembly so the next player to lock it claims it.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class CircuitOwnerCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public string Command => "circuitowner";
    public string Description => "Sets or clears a circuit assembly's owner.";
    public string Help => "Usage: circuitowner <assembly uid> <username|clear>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) ||
            !_entMan.TryGetEntity(netEntity, out var uid) ||
            !_entMan.TryGetComponent<CircuitAssemblyComponent>(uid, out var component))
        {
            shell.WriteError($"'{args[0]}' is not a circuit assembly.");
            return;
        }

        if (args[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            component.OwnerUserId = null;
            component.OwnerName = null;
            component.Locked = false;
            _adminLogger.Add(LogType.AdminMessage, LogImpact.Medium,
                $"{shell.Player?.Name ?? "Console"} cleared the owner of {_entMan.ToPrettyString(uid.Value)}");
            shell.WriteLine("Circuit assembly ownership cleared.");
            return;
        }

        if (!_players.TryGetSessionByUsername(args[1], out var session))
        {
            shell.WriteError($"No player named '{args[1]}'.");
            return;
        }

        component.OwnerUserId = session.UserId.ToString();
        component.OwnerName = session.Name;
        component.Locked = true;
        _adminLogger.Add(LogType.AdminMessage, LogImpact.Medium,
            $"{shell.Player?.Name ?? "Console"} set the owner of {_entMan.ToPrettyString(uid.Value)} to {session.Name}");
        shell.WriteLine($"Circuit assembly owner set to {session.Name} and locked.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<CircuitAssemblyComponent>(args[0], _entMan),
                "assembly"),
            2 => CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(players: _players),
                "username|clear"),
            _ => CompletionResult.Empty,
        };
    }
}
