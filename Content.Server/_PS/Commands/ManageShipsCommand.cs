using Content.Server._PS.BluespaceDock;
using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._PS.Commands;

/// <summary>
/// Palmtree: opens the admin ship manager - player ships (by deed, so offline owners are included)
/// with a live preview and a delete button.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ManageShipsCommand : IConsoleCommand
{
    [Dependency] private readonly EuiManager _eui = default!;

    public string Command => "manageships";
    public string Description => "Lists player ships and lets you preview or delete them.";
    public string Help => Command;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError("This command can only be run by a player.");
            return;
        }

        _eui.OpenEui(new ManageShipsEui(), player);
    }
}
