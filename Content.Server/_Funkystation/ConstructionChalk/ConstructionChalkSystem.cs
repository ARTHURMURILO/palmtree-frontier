// SPDX-FileCopyrightText: 2025 Funky Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Construction;
using Content.Shared._Funkystation.ConstructionChalk;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Funkystation.ConstructionChalk;

public sealed partial class ConstructionChalkSystem : SharedConstructionChalkSystem
{
    [Dependency] private IPrototypeManager _proto = null!;
    [Dependency] private ConstructionSystem _construction = null!;
    [Dependency] private SharedInteractionSystem _interaction = null!;
    [Dependency] private SharedHandsSystem _hands = null!;
    [Dependency] private SharedTransformSystem _transform = null!;
    [Dependency] private SharedAudioSystem _audio = null!;
    [Dependency] private SharedPhysicsSystem _physics = null!;
    [Dependency] private EntityLookupSystem _lookup = null!;
    [Dependency] private IGameTiming _timing = null!;

    private const string ChalkMarkPrototype = "ConstructionChalkMark";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ChalkPlaceMarkEvent>(OnPlaceMark);
        SubscribeLocalEvent<ConstructionChalkMarkComponent, InteractUsingEvent>(OnMarkInteractUsing);
        SubscribeLocalEvent<ConstructionChalkComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<ConstructionChalkComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
    }

    private void OnPlaceMark(ChalkPlaceMarkEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        if (!TryGetEntity(ev.Chalk, out var chalkUid) ||
            !TryComp<ConstructionChalkComponent>(chalkUid.Value, out var chalkComp) ||
            !_hands.IsHolding(user, chalkUid.Value))
        {
            return;
        }

        if (_timing.CurTime < chalkComp.NextPlace)
            return;

        chalkComp.NextPlace = _timing.CurTime + TimeSpan.FromSeconds(0.25);

        var coords = GetCoordinates(ev.Coordinates);
        var mapPos = _transform.ToMapCoordinates(coords);
        if (mapPos.MapId == MapId.Nullspace)
            return;

        if (!IsChalkableRecipe(ev.ConstructionPrototype, chalkComp.Mode) ||
            !_proto.TryIndex<ConstructionPrototype>(ev.ConstructionPrototype, out var recipe))
        {
            return;
        }

        // just checks the same recipe...
        if (MarkAlreadyPresent(mapPos, ev.ConstructionPrototype))
            return;

        bool IgnoreOccupied(EntityUid e)
        {
            if (!TryComp<FixturesComponent>(e, out var fixtures))
                return false;

            var aabb = _physics.GetWorldAABB(e, fixtures);
            return aabb.Contains(mapPos.Position);
        }

        if (!_interaction.InRangeUnobstructed(user, mapPos, predicate: IgnoreOccupied))
            return;

        if (recipe.Conditions.Any(condition => !condition.Condition(user, coords, ev.Rotation.GetCardinalDir())))
        {
            return;
        }

        var mark = Spawn(ChalkMarkPrototype, coords);
        var comp = EnsureComp<ConstructionChalkMarkComponent>(mark);
        comp.ConstructionPrototype = ev.ConstructionPrototype;
        comp.Rotation = recipe.CanRotate ? ev.Rotation : Angle.Zero;
        Dirty(mark, comp);
        _transform.SetLocalRotation(mark, comp.Rotation);

        _audio.PlayPvs(chalkComp.PlaceSound, mark);
    }

    private void OnMarkInteractUsing(Entity<ConstructionChalkMarkComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !IsValidBuildItem(ent.Comp, args.Used))
            return;

        args.Handled = true;
        TryBuildFromMark(ent, args.User);
    }

    private bool IsValidBuildItem(ConstructionChalkMarkComponent mark, EntityUid used)
    {
        if (mark.ConstructionPrototype is not { } protoId ||
            !_proto.TryIndex(protoId, out var recipe) ||
            !_proto.TryIndex(recipe.Graph, out var graph))
        {
            return false;
        }

        var start = graph.Nodes[recipe.StartNode];
        var target = graph.Nodes[recipe.TargetNode];
        var path = graph.Path(start.Name, target.Name);
        if (path == null || path.Length == 0)
            return false;

        var edge = start.GetEdge(path[0].Name);
        if (edge == null)
            return false;

        foreach (var step in edge.Steps)
        {
            if (step is EntityInsertConstructionGraphStep insert &&
                insert.EntityValid(used, EntityManager, Factory))
            {
                return true;
            }
        }

        return false;
    }

    private async void TryBuildFromMark(Entity<ConstructionChalkMarkComponent> mark, EntityUid user)
    {
        if (mark.Comp.IsBuilding)
            return;

        if (mark.Comp.ConstructionPrototype is not { } protoId)
        {
            QueueDel(mark.Owner);
            return;
        }

        mark.Comp.IsBuilding = true;

        try
        {
            if (!_proto.TryIndex(protoId, out var recipe))
            {
                QueueDel(mark.Owner);
                return;
            }

            var coords = Transform(mark.Owner).Coordinates;
            var rotation = mark.Comp.Rotation;

            if (Deleted(mark.Owner))
                return;

            var structure = await _construction.TryStartStructureConstructionAt(user, recipe, coords, rotation);

            if (structure is not { Valid: true })
            {
                if (!Deleted(mark.Owner))
                    mark.Comp.IsBuilding = false;
                return;
            }

            if (!Deleted(mark.Owner))
                QueueDel(mark.Owner);
        }
        catch (Exception e)
        {
            Log.Error($"Error while building from chalk mark: {e}");
            if (!Deleted(mark.Owner))
                mark.Comp.IsBuilding = false;
        }
    }

    private bool IsChalkableRecipe(string constructionPrototype, ChalkMode mode)
    {
        foreach (var category in _proto.EnumeratePrototypes<ChalkCategoryPrototype>())
        {
            if (category.Mode != mode)
                continue;

            foreach (var entry in category.Entries)
            {
                if (entry.ConstructionPrototype.Id == constructionPrototype)
                    return true;

                if (_proto.TryIndex<ConstructionPrototype>(entry.ConstructionPrototype.Id, out var baseRecipe) &&
                    baseRecipe.AlternativePrototypes.Any(alt => alt.Id == constructionPrototype))
                    return true;
            }
        }

        return false;
    }

    private bool MarkAlreadyPresent(MapCoordinates mapPos, string constructionPrototype)
    {
        foreach (var existing in _lookup.GetEntitiesInRange<ConstructionChalkMarkComponent>(mapPos, 0.1f))
        {
            if (existing.Comp.ConstructionPrototype is not { } existingProtoId)
                continue;

            if (existingProtoId.Id == constructionPrototype)
                return true;
        }

        return false;
    }
}
