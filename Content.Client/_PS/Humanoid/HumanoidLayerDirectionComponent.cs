using Robust.Shared.Graphics.RSI;

namespace Content.Client._PS.Humanoid;

/// <summary>
/// Palmtree: overrides the screen direction used when ordering humanoid sprite layers. The lobby
/// editor renders its preview through <c>SpriteView.OverrideDirection</c> without rotating the dummy
/// entity, so the direction-aware layer system needs the displayed direction explicitly.
/// </summary>
[RegisterComponent]
public sealed partial class HumanoidLayerDirectionComponent : Component
{
    [DataField]
    public RsiDirection Direction = RsiDirection.South;
}
