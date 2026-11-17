using Content.Shared.Atmos.Components;
using Content.Shared.Construction.EntitySystems;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared.Construction.Conditions;

/// <summary>
/// Palmtree: like <see cref="NoUnstackableInTile"/>, but only counts unstackable atmos devices on
/// the pipe layer this construction will use. Two vents on different atmos layers may share a tile,
/// while two on the same layer still may not.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class NoUnstackableOnPipeLayer : IConstructionCondition
{
    [DataField]
    public AtmosPipeLayer Layer = AtmosPipeLayer.Primary;

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var sysMan = IoCManager.Resolve<IEntitySystemManager>();
        var anchorable = sysMan.GetEntitySystem<AnchorableSystem>();

        return !anchorable.AnyUnstackablesAnchoredAt(location, Layer);
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = NoUnstackableInTile.GuidebookString
        };
    }
}
