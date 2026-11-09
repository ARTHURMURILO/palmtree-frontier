using Content.Shared.Maps;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Map;
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

            // A point lookup misses a directional window's edge fixture. Query the whole tile
            // instead, or a second window can be built straight through the first one.
            var entities = new HashSet<EntityUid>();
            turfSystem.GetEntitiesInTile(location, entities, LookupFlags.Static | LookupFlags.Dynamic);

            foreach (var entity in entities)
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
