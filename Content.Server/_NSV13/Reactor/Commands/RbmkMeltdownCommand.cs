// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server._NSV13.Reactor.Systems;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._NSV13.Reactor.Commands;

/// <summary>
///     Detonates an RBMK on demand, so the failure path can be exercised without first building a
///     fuelled, cooled reactor by hand.
/// </summary>
/// <remarks>
///     Debug-only, and deliberately so: melting a reactor is irreversible and is supposed to be
///     something a crew earns, not something an admin typo hands out. With no argument it picks the
///     nearest reactor to the caller, which is usually the one you are standing in front of.
/// </remarks>
[AdminCommand(AdminFlags.Debug)]
public sealed class RbmkMeltdownCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "rbmk_meltdown";
    public string Description => "Detonates an RBMK reactor. No argument targets the nearest reactor to you.";
    public string Help => "rbmk_meltdown [entityUid]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError("Expected at most one entity id.");
            return;
        }

        var reactors = _entMan.System<RbmkReactorSystem>();
        EntityUid target = EntityUid.Invalid;

        if (args.Length == 1)
        {
            if (!NetEntity.TryParse(args[0], out var netEnt) ||
                !_entMan.TryGetEntity(netEnt, out var uid) ||
                uid is not { } resolved)
            {
                shell.WriteError($"Unknown entity '{args[0]}'.");
                return;
            }

            target = resolved;
        }
        else
        {
            if (shell.Player is not { AttachedEntity: { } attacker })
            {
                shell.WriteError("Run this from a player session, or pass a reactor entity id.");
                return;
            }

            var origin = _entMan.GetComponent<TransformComponent>(attacker).Coordinates;
            var best = float.MaxValue;

            var query = _entMan.EntityQueryEnumerator<RbmkReactorComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                var distance = (_entMan.GetComponent<TransformComponent>(uid).Coordinates.Position - origin.Position)
                    .LengthSquared();

                if (distance >= best)
                    continue;

                best = distance;
                target = uid;
            }

            if (target == EntityUid.Invalid)
            {
                shell.WriteError("No reactor found on this map.");
                return;
            }
        }

        if (!_entMan.TryGetComponent<RbmkReactorComponent>(target, out var reactor))
        {
            shell.WriteError($"{_entMan.ToPrettyString(target)} is not an RBMK reactor.");
            return;
        }

        if (reactor.Slagged)
        {
            shell.WriteError($"{_entMan.ToPrettyString(target)} has already melted down.");
            return;
        }

        // Deliberately no confirmation: this is a debug flag, and the whole point is speed.
        reactors.Meltdown(target, reactor);
        shell.WriteLine($"Detonated {_entMan.ToPrettyString(target)}.");
    }
}
