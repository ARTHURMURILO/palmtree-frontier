// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Server.Administration;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Administration;
using Content.Shared.Atmos;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._NSV13.Reactor.Commands;

/// <summary>
///     Operates a Stormdrive without walking up to its control console.
/// </summary>
/// <remarks>
///     Debug-only. The real machine is driven by <see cref="Systems.StormdriveConsoleSystem"/>; this
///     command exists to exercise the core without piping a ship, and <c>start</c> also primes the
///     fuel line because ignition otherwise needs a particle accelerator that this port does not have.
/// </remarks>
[AdminCommand(AdminFlags.Debug)]
public sealed class StormdriveCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "stormdrive";
    public string Description => "Debug control for the nearest storm drive.";
    public string Help => "stormdrive <status|prime|start|rods|meltdown> [value]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var drive = FindNearest(shell);

        if (drive is not { } uid || !_entMan.TryGetComponent<StormdriveComponent>(uid, out var comp))
        {
            shell.WriteError("No storm drive found on your map.");
            return;
        }

        var system = _entMan.System<StormdriveSystem>();

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                shell.WriteLine($"{_entMan.ToPrettyString(uid)}");
                shell.WriteLine($"  state={comp.State} heat={comp.Heat:F1}C power={comp.LastPowerProduced:F0}W");
                shell.WriteLine($"  rods={comp.ControlRodPercent:F1}% modifier={comp.ControlRodModifier:F2} integrity={comp.ControlRodIntegrity:F1}");
                shell.WriteLine($"  reaction={comp.ReactionRate:F2} mol/tick stability={comp.ReactorStability:F1} warning={comp.WarningState}");
                return;

            case "prime":
                Prime(uid, comp);
                shell.WriteLine("Fuel line primed with 50 mol constricted plasma; core pre-heated.");
                return;

            case "start":
                Prime(uid, comp);

                if (system.TryStart(uid, comp))
                {
                    shell.WriteLine("Ignition successful.");
                    return;
                }

                shell.WriteError("Ignition failed even after priming. Check the fuel port is connected.");
                return;

            case "rods":
                if (args.Length != 2 || !float.TryParse(args[1], out var percent))
                {
                    shell.WriteError("Usage: stormdrive rods <0-100>");
                    return;
                }

                // Share the console path so the debug command has the same clamping and the same
                // rod-wear / overheat side effects as the real slider.
                system.SetControlRods(uid, comp, percent);
                shell.WriteLine($"Control rods set to {comp.ControlRodPercent:F1}%.");
                return;

            case "meltdown":
                system.StartMeltdown(uid, comp);
                shell.WriteLine($"Meltdown armed; it fires in {comp.MeltdownDelay.TotalSeconds:F0} seconds unless the core cools below {comp.TemperatureMeltdown:F0} C.");
                return;

            default:
                shell.WriteError(Help);
                return;
        }
    }

    /// <summary>
    ///     Debug shortcut standing in for the particle accelerator: enough constricted plasma to
    ///     satisfy ignition, and enough pre-heat to pass the gate.
    /// </summary>
    private void Prime(EntityUid uid, StormdriveComponent comp)
    {
        var nodes = _entMan.System<NodeContainerSystem>();

        if (nodes.TryGetNode(uid, comp.FuelInputName, out PipeNode? fuel))
            fuel.Air.AdjustMoles(Gas.ConstrictedPlasma, 50f);

        if (comp.Heat < comp.StartThreshold)
            comp.Heat = comp.StartThreshold;
    }

    private EntityUid? FindNearest(IConsoleShell shell)
    {
        if (shell.Player is not { AttachedEntity: { } body })
            return null;

        var origin = _entMan.GetComponent<TransformComponent>(body).Coordinates;

        EntityUid? best = null;
        var bestDistance = float.MaxValue;

        var query = _entMan.EntityQueryEnumerator<StormdriveComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var distance = (_entMan.GetComponent<TransformComponent>(uid).Coordinates.Position - origin.Position)
                .LengthSquared();

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = uid;
        }

        return best;
    }
}
