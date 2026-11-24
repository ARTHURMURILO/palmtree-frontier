// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus <90893484+LaCumbiaDelCoronavirus@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 marc-pelletier <113944176+marc-pelletier@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Server._Funkystation.Atmos.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared._Funkystation.Atmos.Visuals;
using Content.Server._Funkystation.Atmos.HFR.Systems;

namespace Content.Server._Funkystation.Atmos.Systems;

public sealed class HFRWasteOutputSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly HFRCoreSystem _coreSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly HypertorusFusionReactorSystem _hfrSystem = default!;
    [Dependency] private readonly HFRSidePartSystem _hfrSidePartSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HFRWasteOutputComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<HFRWasteOutputComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnAnchorChanged(EntityUid uid, HFRWasteOutputComponent wasteOutput, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
        {
            if (wasteOutput.CoreUid != null)
            {
                if (EntityManager.TryGetComponent<HFRCoreComponent>(wasteOutput.CoreUid, out var coreComp))
                {
                    wasteOutput.IsActive = false;
                    if (TryComp<AppearanceComponent>(uid, out var appearance))
                    {
                        _appearanceSystem.SetData(uid, HFRVisuals.IsActive, false, appearance);
                    }

                    coreComp.WasteOutputUid = null;
                    _hfrSystem.ToggleActiveState(wasteOutput.CoreUid.Value, coreComp, false);
                }
                wasteOutput.CoreUid = null;
            }
        }
        else
        {
            _hfrSidePartSystem.TryFindCore(uid);
        }
    }

    private void OnShutdown(EntityUid uid, HFRWasteOutputComponent wasteOutput, ref ComponentShutdown args)
    {
        // Palmtree: a deleted part must not stay linked from the core; the core would otherwise keep
        // treating a recycled UID as its waste output link.
        if (wasteOutput.CoreUid is { } coreUid && EntityManager.TryGetComponent<HFRCoreComponent>(coreUid, out var core))
        {
            if (core.WasteOutputUid == uid)
                core.WasteOutputUid = null;
        }

        wasteOutput.CoreUid = null;
    }
}