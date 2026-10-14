using System.Collections.Generic;
using System.Linq;
using Content.Server.Construction.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Research.Prototypes;
using Content.Shared.Construction.Components;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._NF;

/// <summary>
/// Palmtree: the tiered thruster set ported from coyote-frontier (thrusters_but_cooler.yml) is wired
/// up: every thruster has a machine board, the board builds it back, and the boards are reachable
/// through research and the engineering/blueprint lathe packs.
/// </summary>
[TestFixture]
public sealed class TieredThrusterTest
{
    private static readonly (string Thruster, string Board, float BaseThrust)[] Thrusters =
    [
        ("SlowWeakThruster", "SlowWeakThrusterMachineCircuitboard", 33),
        ("SlowStrongThruster", "SlowStrongThrusterMachineCircuitboard", 200),
        ("FastWeakThruster", "FastWeakThrusterMachineCircuitboard", 33),
        ("FastStrongThruster", "FastStrongThrusterMachineCircuitboard", 100),
        ("FasterWeakThruster", "FasterWeakThrusterMachineCircuitboard", 33),
        ("FasterStrongThruster", "FasterStrongThrusterMachineCircuitboard", 330),
    ];

    [Test]
    public async Task TieredThrustersAreWired()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var packs = proto.EnumeratePrototypes<LatheRecipePackPrototype>()
                .ToDictionary(p => p.ID, p => p.Recipes);
            var blueprints = proto.EnumeratePrototypes<BlueprintPrototype>()
                .SelectMany(b => b.Packs)
                .SelectMany(p => packs.GetValueOrDefault(p, new HashSet<ProtoId<LatheRecipePrototype>>()))
                .ToHashSet();

            foreach (var (thrusterId, boardId, baseThrust) in Thrusters)
            {
                var thrusterProto = proto.Index<EntityPrototype>(thrusterId);
                Assert.That(thrusterProto.Abstract, Is.False, $"{thrusterId} should be spawnable.");

                // The ported stats survived the port (baseThrust drives the shuttle's max speed).
                var thruster = (ThrusterComponent) thrusterProto.Components["Thruster"].Component;
                Assert.That(thruster.BaseThrust, Is.EqualTo(baseThrust), $"{thrusterId} baseThrust changed.");
                Assert.That(thruster.ThrustPerPartLevel, Is.Not.Empty, $"{thrusterId} needs upgrade levels.");

                // The thruster builds from its own board and the board builds the thruster back.
                var machine = (MachineComponent) thrusterProto.Components["Machine"].Component;
                Assert.That(machine.Board?.Id, Is.EqualTo(boardId), $"{thrusterId} should use {boardId}.");

                var boardProto = proto.Index<EntityPrototype>(boardId);
                var board = (MachineBoardComponent) boardProto.Components["MachineBoard"].Component;
                Assert.That(board.Prototype.Id, Is.EqualTo(thrusterId), $"{boardId} should build {thrusterId}.");

                // A lathe recipe exists and is in the blueprint pool (the tech tree requires that).
                Assert.That(proto.HasIndex<LatheRecipePrototype>(boardId), Is.True, $"{boardId} needs a lathe recipe.");
                Assert.That(blueprints, Contains.Item(boardId), $"{boardId} must be printable to a blueprint.");

                // ... and the engineering lathe can print it once researched.
                Assert.That(packs["NFEngineeringBoards"], Contains.Item(boardId), $"{boardId} missing from NFEngineeringBoards.");
            }

            // The slow pair unlocks with the standard shuttle tech, the fast four with the small one.
            var research = proto.EnumeratePrototypes<TechnologyPrototype>().ToList();
            var slowTech = research.Single(t => t.ID == "NFShuttlecraftThrust");
            var fastTech = research.Single(t => t.ID == "NFShuttlecraftThrustSmall");
            Assert.That(slowTech.RecipeUnlocks, Contains.Item("SlowWeakThrusterMachineCircuitboard"));
            Assert.That(slowTech.RecipeUnlocks, Contains.Item("SlowStrongThrusterMachineCircuitboard"));
            foreach (var board in new[]
                     {
                         "FastWeakThrusterMachineCircuitboard",
                         "FastStrongThrusterMachineCircuitboard",
                         "FasterWeakThrusterMachineCircuitboard",
                         "FasterStrongThrusterMachineCircuitboard",
                     })
            {
                Assert.That(fastTech.RecipeUnlocks, Contains.Item(board), $"{board} should unlock with NFShuttlecraftThrustSmall.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
