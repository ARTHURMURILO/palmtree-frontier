using System.Linq;
using Content.Server._PS.BluespaceDock;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._PS.Commands;

/// <summary>
/// Palmtree: stores a player's ship in the bluespace dock without requiring it to be docked. Living
/// things aboard are moved off the ship instead of refusing the store; every other check still runs
/// (civilian vessel, not already being processed, and so on) and failures are printed to the console.
/// No tax is charged.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ForceDockCommand : IConsoleCommand
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    public string Command => "forcedock";
    public string Description => "Stores a player's ship in the bluespace dock without requiring it to be docked.";
    public string Help => $"{Command} <ship name | grid uid> [owner]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        var dock = _entMan.System<BluespaceDockSystem>();
        var ships = dock.GetDeedShips();

        if (args[0] == "?")
        {
            shell.WriteLine(ships.Count == 0
                ? "No player ships are in the world."
                : string.Join(", ", ships.Select(s => s.Name)));
            return;
        }

        // The ship: a grid uid, or a name (case-insensitive).
        BluespaceDockSystem.DeedShip ship;
        if (NetEntity.TryParse(args[0], out var netShip) && _entMan.TryGetEntity(netShip, out var shipUid))
        {
            var found = ships.Where(s => s.Grid == shipUid.Value).ToList();
            if (found.Count == 0)
            {
                shell.WriteError($"Grid {args[0]} is not a player ship (it has no deed).");
                return;
            }

            ship = found[0];
        }
        else
        {
            var matches = ships.Where(s => string.Equals(s.Name, args[0], StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                shell.WriteError($"No player ship is named '{args[0]}'.");
                return;
            }

            if (matches.Count > 1)
            {
                shell.WriteError($"Several ships are named '{args[0]}'; pass the grid uid instead.");
                return;
            }

            ship = matches[0];
        }

        // The owner defaults to the ship's deed owner. When the name matches a player in the round,
        // their ckey is recorded too; otherwise the entry is keyed by character name alone.
        var ownerName = args.Length == 2 ? args[1].Trim() : ship.Owner;
        if (ownerName.Length == 0)
        {
            shell.WriteError("That ship has no owner on its deed; pass one explicitly.");
            return;
        }

        var ownerUserId = string.Empty;
        foreach (var session in _player.Sessions)
        {
            if (string.Equals(dock.GetCharacterName(session.UserId, null, session.AttachedEntity), ownerName,
                    StringComparison.Ordinal))
            {
                ownerUserId = session.UserId.ToString();
                break;
            }
        }

        // Every store check except "must be docked to this station" and "nothing living aboard":
        // force-dock kicks the occupants out instead of refusing the ship.
        if (dock.FindStoreBlocker(ship.Grid, requireDocked: false, stationGrid: null, allowOrganics: true) is { } blocker)
        {
            shell.WriteError(Loc.GetString(blocker));
            return;
        }

        // Get everyone off first so the console can say how many were moved. TryStoreShip evacuates
        // again, which is a no-op by then.
        var evacuated = dock.EvacuateGrid(ship.Grid);

        var value = dock.AppraiseShip(ship.Grid);
        if (!dock.TryStoreShip(ship.Grid, ownerUserId, ownerName, value, payer: null, cost: 0,
                out var error, out var skipped))
        {
            shell.WriteError(Loc.GetString(error ?? "bluespace-dock-error-save-failed"));
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{shell.Player?.Name ?? "Console"} force-docked ship {ship.Name} into the bluespace dock as {ownerName} ({ownerUserId})");

        shell.WriteLine($"Stored {ship.Name} for {ownerName} in the bluespace dock. No tax was charged.");

        if (evacuated > 0)
            shell.WriteLine($"Moved {evacuated} living thing(s) (or ghosts/corpses) off {ship.Name} before storing it.");

        if (skipped > 0)
        {
            shell.WriteError($"{skipped} item(s) could not be saved with {ship.Name} and will come back " +
                             "as fresh copies when the ship is retrieved (see the server log for which).");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var names = _entMan.System<BluespaceDockSystem>().GetDeedShips().Select(s => s.Name);
            return CompletionResult.FromHintOptions(names, "<ship name | grid uid | ?>");
        }

        if (args.Length == 2)
        {
            var dock = _entMan.System<BluespaceDockSystem>();
            var owners = _player.Sessions
                .Select(session => dock.GetCharacterName(session.UserId, null, session.AttachedEntity))
                .Where(name => name.Length > 0);
            return CompletionResult.FromHintOptions(owners, "[owner (defaults to the deed owner)]");
        }

        return CompletionResult.Empty;
    }
}
