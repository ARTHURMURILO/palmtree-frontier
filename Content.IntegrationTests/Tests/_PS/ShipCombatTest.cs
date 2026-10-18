using System.Collections.Generic;
using Content.Server._PS.ShipCombat;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the per-grid ship combat state (<see cref="ShipCombatComponent"/>) and its condition.
/// </summary>
[TestFixture]
public sealed class ShipCombatTest
{
    /// <summary>Records condition changes raised on grids.</summary>
    public sealed class ShipCombatTestSystem : EntitySystem
    {
        public List<ShipCombatConditionChangedEvent> ConditionChanges { get; } = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ShipCombatComponent, ShipCombatConditionChangedEvent>(OnConditionChanged);
        }

        private void OnConditionChanged(EntityUid uid, ShipCombatComponent component, ShipCombatConditionChangedEvent args)
        {
            ConditionChanges.Add(args);
        }
    }

    [Test]
    public async Task EnsureAddsStateToGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();

            Assert.That(entMan.HasComponent<ShipCombatComponent>(grid), Is.False,
                "The test grid should not start with ship combat state.");

            Assert.That(combat.TryEnsureShipCombat(grid, out var state), Is.True,
                "Ensuring ship combat state on a grid failed.");
            Assert.That(state.Owner, Is.EqualTo(grid));
            Assert.That(state.Comp.Condition, Is.EqualTo(ShipCondition.Green),
                "A new ship should start at GREEN (peacetime).");
            Assert.That(state.Comp.AutomaticCondition, Is.True,
                "Automatic condition should default to on.");

            // Idempotent: the existing component comes back instead of being replaced.
            Assert.That(combat.TryEnsureShipCombat(grid, out var again), Is.True);
            Assert.That(again.Comp, Is.SameAs(state.Comp));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EnsureResolvesGridFromEntityOnIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = testMap.Grid.Owner;

        EntityUid device = default;
        await server.WaitPost(() =>
        {
            device = entMan.SpawnEntity("APCBasic", testMap.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryEnsureShipCombat(device, out var state), Is.True,
                "A device on the grid should resolve to the grid's state.");
            Assert.That(state.Owner, Is.EqualTo(grid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EnsureFailsForEntityInSpace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        EntityUid freeEntity = default;
        await server.WaitPost(() =>
        {
            mapSystem.CreateMap(out var mapId);
            freeEntity = entMan.SpawnEntity(null, new MapCoordinates(0f, 0f, mapId));
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryEnsureShipCombat(freeEntity, out _), Is.False,
                "An entity that is not on a grid must not get ship combat state.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SetConditionChangesAndRaisesEvent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var grid = testMap.Grid.Owner;

        var listener = server.System<ShipCombatTestSystem>();

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TryEnsureShipCombat(grid, out _), Is.True);

            Assert.That(combat.TrySetCondition(grid, ShipCondition.Yellow), Is.True);
            Assert.That(combat.TryGetShipCombat(grid, out var state), Is.True);
            Assert.That(state.Comp.Condition, Is.EqualTo(ShipCondition.Yellow));

            Assert.That(listener.ConditionChanges, Has.Count.EqualTo(1));
            Assert.That(listener.ConditionChanges[0].Grid, Is.EqualTo(grid));
            Assert.That(listener.ConditionChanges[0].OldCondition, Is.EqualTo(ShipCondition.Green));
            Assert.That(listener.ConditionChanges[0].NewCondition, Is.EqualTo(ShipCondition.Yellow));

            // Setting the same condition again is a no-op.
            Assert.That(combat.TrySetCondition(grid, ShipCondition.Yellow), Is.True);
            Assert.That(listener.ConditionChanges, Has.Count.EqualTo(1));

            Assert.That(combat.TrySetCondition(grid, ShipCondition.Red), Is.True);
            Assert.That(listener.ConditionChanges, Has.Count.EqualTo(2));
            Assert.That(listener.ConditionChanges[1].OldCondition, Is.EqualTo(ShipCondition.Yellow));
            Assert.That(listener.ConditionChanges[1].NewCondition, Is.EqualTo(ShipCondition.Red));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SetConditionWithoutStateFails()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        EntityUid freeEntity = default;
        await server.WaitPost(() =>
        {
            mapSystem.CreateMap(out var mapId);
            freeEntity = entMan.SpawnEntity(null, new MapCoordinates(0f, 0f, mapId));
        });

        await server.WaitAssertion(() =>
        {
            var combat = server.System<ShipCombatSystem>();
            Assert.That(combat.TrySetCondition(freeEntity, ShipCondition.Red), Is.False,
                "Setting a condition on an entity without ship combat state must fail.");
        });

        await pair.CleanReturnAsync();
    }
}
