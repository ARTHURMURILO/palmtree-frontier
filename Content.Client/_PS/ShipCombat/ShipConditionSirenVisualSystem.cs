using Content.Shared._PS.ShipCombat.Components;
using Robust.Client.GameObjects;

namespace Content.Client._PS.ShipCombat;

/// <summary>
/// Palmtree: toggles the condition siren's sprite between its dark and lit states.
/// </summary>
public sealed class ShipConditionSirenVisualSystem : VisualizerSystem<ShipConditionSirenComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, ShipConditionSirenComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!AppearanceSystem.TryGetData<bool>(uid, ShipConditionSirenVisuals.Active, out var active, args.Component))
            active = false;

        SpriteSystem.LayerSetVisible((uid, args.Sprite), ShipConditionSirenLayers.Off, !active);
        SpriteSystem.LayerSetVisible((uid, args.Sprite), ShipConditionSirenLayers.On, active);
    }
}
