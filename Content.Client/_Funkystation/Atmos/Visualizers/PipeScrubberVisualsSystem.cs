// SPDX-FileCopyrightText: 2026 Steve <marlumpy@gmail.com>
//
// SPDX-License-Identifier: MIT

using Robust.Client.GameObjects;
using Content.Shared._Funkystation.Atmos.Visuals;

namespace Content.Client._Funkystation.Atmos.Visualizers;

public sealed class PipeScrubberVisualizerSystem : VisualizerSystem<PipeScrubberVisualsComponent>
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    protected override void OnAppearanceChange(EntityUid uid, PipeScrubberVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        bool isEnabled;
        bool isFull;
        bool isScrubbing;
        bool isDraining;

        AppearanceSystem.TryGetData<bool>(uid, PipeScrubberVisuals.IsEnabled, out isEnabled, args.Component);
        AppearanceSystem.TryGetData<bool>(uid, PipeScrubberVisuals.IsFull, out isFull, args.Component);
        AppearanceSystem.TryGetData<bool>(uid, PipeScrubberVisuals.IsScrubbing, out isScrubbing, args.Component);
        AppearanceSystem.TryGetData<bool>(uid, PipeScrubberVisuals.IsDraining, out isDraining, args.Component);

        // Layer 0 is the idle body. Layers 1-4 are the enabled light, the scrubbing
        // animation, the full indicator and the draining animation.
        SetLayerState(1, isEnabled, component.EnabledState, uid, args.Sprite);
        SetLayerState(2, isScrubbing, component.ScrubbingState, uid, args.Sprite);
        SetLayerState(3, isFull, component.FullState, uid, args.Sprite);
        SetLayerState(4, isDraining, component.DrainingState, uid, args.Sprite);
    }

    private void SetLayerState(int index, bool visible, string state, EntityUid uid, SpriteComponent? sprite)
    {
        _sprite.LayerSetVisible((uid, sprite), index, visible);
        if (visible)
            _sprite.LayerSetRsiState((uid, sprite), index, state);
    }
}
