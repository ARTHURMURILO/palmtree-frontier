using Content.Server._PS.ShipCombat;
using Content.Server.Administration;
using Content.Shared._PS.ShipCombat;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._PS.Commands;

/// <summary>
/// Palmtree: debug command to set one of the ship power pools on the grid the player is standing on.
/// The engineering console is the intended way to do this; this exists so the power budget can be
/// tested before that UI lands.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class SetShipPowerCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "setshippower";
    public string Description => "Sets a ship power pool (Shields/Weapons/Thrusters/Auxiliary) on the grid you are standing on.";
    public string Help => $"{Command} <pool> <15-125>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError("You must be in-game to use this command.");
            return;
        }

        if (!Enum.TryParse<ShipPowerPool>(args[0], ignoreCase: true, out var pool))
        {
            shell.WriteError($"Unknown pool '{args[0]}'. Pools: Shields, Weapons, Thrusters, Auxiliary.");
            return;
        }

        if (!int.TryParse(args[1], out var value))
        {
            shell.WriteError($"'{args[1]}' is not a number.");
            return;
        }

        var combat = _entMan.System<ShipCombatSystem>();
        if (!combat.TryGetShipCombat(player, out var state))
        {
            shell.WriteError("The grid you are on has no ship combat state (place a tactical console or a bluespace power sink first).");
            return;
        }

        _entMan.System<ShipPowerSystem>().SetPool(state, pool, value);
        shell.WriteLine($"Set {pool} to {ShipPowerSystem.GetPool(state.Comp, pool)} " +
                        $"(Shields {state.Comp.ShieldPower}, Weapons {state.Comp.WeaponPower}, " +
                        $"Thrusters {state.Comp.ThrusterPower}, Auxiliary {state.Comp.AuxiliaryPower}).");
    }
}
