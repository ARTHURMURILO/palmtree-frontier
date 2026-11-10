using Content.Shared.Maps;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.Construction.Conditions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class NoWindowsInTile : IConstructionCondition
    {
        private static readonly ProtoId<TagPrototype> WindowTag = "Window";
        private static readonly ProtoId<TagPrototype> DirectionalWindowTag = "DirectionalWindow";

        /// <summary>
        ///     Palmtree: allow another directional window on the same tile as long as it faces a different
        ///     edge. This is what lets players make a corner/pivot from two directional windows. Same-edge
        ///     duplicates are still rejected.
        /// </summary>
        [DataField("allowDirectionalWindowPivot")] private bool _allowDirectionalWindowPivot = false;

        public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
        {
            var entManager = IoCManager.Resolve<IEntityManager>();
            var sysMan = entManager.EntitySysManager;
            var tagSystem = sysMan.GetEntitySystem<TagSystem>();
            var turfSystem = sysMan.GetEntitySystem<TurfSystem>();
            var mapSystem = sysMan.GetEntitySystem<SharedMapSystem>();

            // Ask the grid which entities are anchored in this exact tile. A broadphase tile lookup
            // also returns windows from the neighbouring tiles, because a directional window's
            // fixture sits right on the tile border, and that made a free edge look occupied.
            if (!turfSystem.TryGetTileRef(location, out var tileRef) ||
                tileRef is not { } tile ||
                !entManager.TryGetComponent(tile.GridUid, out MapGridComponent? grid))
            {
                return true;
            }

            var indices = tile.GridIndices;

            foreach (var entity in mapSystem.GetAnchoredEntities(tile.GridUid, grid, indices))
            {
                if (!tagSystem.HasTag(entity, WindowTag))
                    continue;

                if (_allowDirectionalWindowPivot && tagSystem.HasTag(entity, DirectionalWindowTag))
                {
                    // One window per edge. A window facing a different edge is the other half of a pivot.
                    var xform = entManager.GetComponent<TransformComponent>(entity);
                    if (xform.LocalRotation.GetCardinalDir() != direction)
                        continue;
                }

                return false;
            }

            return true;
        }

        public ConstructionGuideEntry GenerateGuideEntry()
        {
            return new ConstructionGuideEntry
            {
                Localization = _allowDirectionalWindowPivot
                    ? "construction-step-condition-no-windows-on-edge"
                    : "construction-step-condition-no-windows-in-tile"
            };
        }
    }
}
