using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: drives <see cref="ShipConditionLightComponent"/> battle-station lights from the grid's
/// condition: off while at <see cref="ShipConditionLightComponent.OffCondition"/> (RED by default),
/// on at every other condition.
/// </summary>
public sealed class ShipConditionLightSystem : EntitySystem
{
    [Dependency] private readonly SharedPoweredLightSystem _light = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipConditionLightComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipCombatConditionChangedEvent>(OnConditionChanged);
    }

    private void OnMapInit(EntityUid uid, ShipConditionLightComponent component, MapInitEvent args)
    {
        UpdateLight(uid, component);
    }

    private void OnConditionChanged(ShipCombatConditionChangedEvent args)
    {
        var query = EntityQueryEnumerator<ShipConditionLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var light, out var xform))
        {
            if (xform.GridUid != args.Grid)
                continue;

            UpdateLight(uid, light);
        }
    }

    private void UpdateLight(EntityUid uid, ShipConditionLightComponent component)
    {
        // Component tests spawn components in isolation, so the light may not be there.
        if (!TryComp<PoweredLightComponent>(uid, out var light))
            return;

        var condition = TryComp<ShipCombatComponent>(Transform(uid).GridUid, out var combat)
            ? combat.Condition
            : ShipCondition.Green;

        _light.SetState(uid, condition != component.OffCondition, light);
    }
}
