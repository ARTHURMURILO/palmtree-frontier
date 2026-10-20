using Content.Shared._PS.Modsuits;
using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Light.Components;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._PS.Modsuits;

/// <summary>
/// Installs MODsuit module items and applies their generic effects.
/// Specific module effects will be split into behaviour systems as the port grows.
/// </summary>
public sealed class ModsuitModuleSystem : SharedModsuitSystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ModsuitSystem _modsuits = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModsuitControlComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ModsuitControlComponent, ModsuitModulesInitEvent>(OnModulesInit);
    }

    private void OnModulesInit(Entity<ModsuitControlComponent> ent, ref ModsuitModulesInitEvent args)
    {
        if (!Prototypes.TryIndex(ent.Comp.Theme, out var theme))
            return;

        foreach (var moduleProto in theme.InbuiltModules)
        {
            var module = Spawn(moduleProto, Transform(ent.Owner).Coordinates);
            if (!TryInstallModule(ent, module, silent: true))
                QueueDel(module);
        }
    }

    private void OnInteractUsing(Entity<ModsuitControlComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Active)
            return;

        if (!HasComp<ModsuitModuleComponent>(args.Used))
            return;

        if (TryInstallModule(ent, args.Used, silent: false, user: args.User))
            args.Handled = true;
    }

    public bool TryInstallModule(
        Entity<ModsuitControlComponent> ent,
        EntityUid module,
        bool silent,
        EntityUid? user = null)
    {
        if (!TryComp<ModsuitModuleComponent>(module, out var moduleComp))
            return false;

        if (ent.Comp.Modules.Contains(module))
            return false;

        if (!Prototypes.TryIndex(ent.Comp.Theme, out var theme))
            return false;

        var complexity = moduleComp.Complexity;
        foreach (var installed in ent.Comp.Modules)
        {
            if (TryComp<ModsuitModuleComponent>(installed, out var installedComp))
                complexity += installedComp.Complexity;
        }

        if (complexity > theme.ComplexityMax)
        {
            if (!silent)
                _popup.PopupEntity(Loc.GetString("modsuit-module-too-complex"), ent.Owner, user ?? ent.Owner);
            return false;
        }

        if (!_container.Insert(module, ent.Comp.ModuleContainer, force: true))
        {
            if (!silent)
                _popup.PopupEntity(Loc.GetString("modsuit-module-stuck"), ent.Owner, user ?? ent.Owner);
            return false;
        }

        ent.Comp.Modules.Add(module);
        ApplyModuleEffect(ent, module);

        if (!silent)
            _popup.PopupEntity(Loc.GetString("modsuit-module-installed", ("module", Name(module))), ent.Owner, user ?? ent.Owner);

        _modsuits.UpdateUi(ent);
        return true;
    }

    public bool TryUninstallModule(Entity<ModsuitControlComponent> ent, EntityUid module, bool silent = false)
    {
        if (!ent.Comp.Modules.Remove(module))
            return false;

        _container.Remove(module, ent.Comp.ModuleContainer, force: true);

        if (TryComp<ModsuitFlashlightModuleComponent>(module, out _) && !HasModule<ModsuitFlashlightModuleComponent>(ent))
        {
            if (TryComp<UnpoweredFlashlightComponent>(ent.Owner, out var light))
                _actions.RemoveAction(ent.Owner, light.ToggleActionEntity);
            RemComp<UnpoweredFlashlightComponent>(ent.Owner);
            RemComp<PointLightComponent>(ent.Owner);
        }

        if (TryComp<ModsuitMagbootsModuleComponent>(module, out _) && !HasModule<ModsuitMagbootsModuleComponent>(ent))
        {
            foreach (var (slot, part) in ent.Comp.Parts)
            {
                if (slot != ModsuitPartSlot.Boots)
                    continue;

                if (TryComp<ToggleClothingComponent>(part, out var toggle))
                    _actions.RemoveAction(part, toggle.ActionEntity);
                RemComp<ToggleClothingComponent>(part);
                RemComp<ItemToggleComponent>(part);
                RemComp<MagbootsComponent>(part);
                break;
            }
        }

        _modsuits.UpdateUi(ent);
        return true;
    }

    private bool HasModule<T>(Entity<ModsuitControlComponent> ent) where T : IComponent
    {
        foreach (var module in ent.Comp.Modules)
        {
            if (HasComp<T>(module))
                return true;
        }

        return false;
    }

    private void ApplyModuleEffect(Entity<ModsuitControlComponent> ent, EntityUid module)
    {
        if (TryComp<ModsuitStorageModuleComponent>(module, out var storageModule))
        {
            var storage = EnsureComp<StorageComponent>(ent.Owner);
            foreach (var box in storageModule.Grid)
                storage.Grid.Add(box);
        }

        if (TryComp<ModsuitMagbootsModuleComponent>(module, out _))
        {
            foreach (var (slot, part) in ent.Comp.Parts)
            {
                if (slot == ModsuitPartSlot.Boots)
                {
                    AttachMagboots(part);
                    break;
                }
            }
        }

        if (TryComp<ModsuitFlashlightModuleComponent>(module, out _))
        {
            var light = EnsureComp<UnpoweredFlashlightComponent>(ent.Owner);
            EnsureComp<PointLightComponent>(ent.Owner);
            if (_pointLight.TryGetLight(ent.Owner, out var pointLight))
                _pointLight.SetEnabled(ent.Owner, false, pointLight);
            _actions.AddAction(ent.Owner, ref light.ToggleActionEntity, light.ToggleAction, ent.Owner);
        }
    }


}
