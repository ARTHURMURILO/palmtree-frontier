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
/// Palmtree: stores a player's ship in the bluespace dock without requiring it to be docked or to
/// belong to an allowed ship category. Living things aboard are moved off the ship instead of
/// refusing the store; every other check still runs (not already being processed, and so on) and
/// failures are printed to the console. No tax is charged.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ForceDockCommand : IConsoleCommand
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    /// <summary>Safes every reactor on the ship before the store checks run.</summary>
    private const string ScramFlag = "--scram";

    public string Command => "forcedock";
    public string Description => "Stores a player's ship in the bluespace dock without requiring it to be docked.";
    public string Help => $"{Command} <ship name | grid uid> [owner] [--scram]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // Palmtree: `--scram` safes every reactor on the ship before the store checks run. Without
        // it a live core is refused exactly like the console refuses it - the flag is the admin's
        // explicit opt-in, not a silent bypass.
        var scram = args.Contains(ScramFlag, StringComparer.OrdinalIgnoreCase);
        args = args.Where(arg => !string.Equals(arg, ScramFlag, StringComparison.OrdinalIgnoreCase)).ToArray();

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

        // Palmtree: the reactor check is not bypassable by force-dock. `--scram` safes the cores
        // first and then the ordinary check passes; without it the command refuses, so an admin
        // cannot smuggle a live core into storage by accident.
        var safed = scram ? dock.SafeReactors(ship.Grid) : 0;

        // Every store check except the docking, ship-category, dangerous-engine and "nothing living aboard" rules:
        // force-dock kicks the occupants out and purges dangerous engine entities instead of refusing the ship.
        if (dock.FindStoreBlocker(ship.Grid, requireDocked: false, stationGrid: null, allowOrganics: true,
                allowDangerousEngines: true, requireAllowedGroup: false) is { } blocker)
        {
            shell.WriteError(Loc.GetString(blocker));
            return;
        }

        // Palmtree: an admin force-dock is allowed to clean dangerous engine state out of the
        // grid instead of refusing the ship. Normal console storage is refused by the dock.
        var purged = dock.PurgeDangerousShipEntities(ship.Grid);

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

        var scramNote = safed > 0 ? $", scarming {safed} live reactor(s) first" : string.Empty;
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{shell.Player?.Name ?? "Console"} force-docked ship {ship.Name} into the bluespace dock as {ownerName} ({ownerUserId}){scramNote}");

        shell.WriteLine($"Stored {ship.Name} for {ownerName} in the bluespace dock. No tax was charged.");

        if (safed > 0)
            shell.WriteLine($"Safed {safed} reactor(s) on {ship.Name} before storing it.");

        if (evacuated > 0)
            shell.WriteLine($"Moved {evacuated} living thing(s) (or ghosts/corpses) off {ship.Name} before storing it.");

        if (purged > 0)
            shell.WriteLine($"Deleted {purged} supermatter/singularity/tesla entit{(purged == 1 ? "y" : "ies")} from {ship.Name} before storing it.");

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
            // Include deed owners as well as connected sessions: an offline/SSD owner has no
            // session to read a character name from, but their name is still on the ship's deed
            // and is a valid owner argument (the entry is then keyed by character name alone).
            var owners = dock.GetDeedShips()
                .Select(ship => ship.Owner)
                .Concat(_player.Sessions
                    .Select(session => dock.GetCharacterName(session.UserId, null, session.AttachedEntity)))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal);
            return CompletionResult.FromHintOptions(owners, "[owner (defaults to the deed owner)]");
        }

        return CompletionResult.Empty;
    }
}
