using Content.Server._PS.BluespaceDock;
using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._PS.Commands;

/// <summary>
/// Palmtree: opens the admin tool for orphaned ship files (ship files with no dock entry). It lists
/// them, previews one on a frozen map, restores one into a character's dock or deletes it.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class RestoreOrphanShipCommand : IConsoleCommand
{
    [Dependency] private readonly EuiManager _eui = default!;

    public string Command => "restoreorphanship";
    public string Description => "Lists orphaned ship files and lets you preview, restore (into a character's dock) or delete them.";
    public string Help => Command;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError("This command can only be run by a player.");
            return;
        }

        _eui.OpenEui(new RestoreOrphanShipEui(), player);
    }
}
