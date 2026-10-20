using Content.Shared.Actions;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// Shared MODsuit helpers that need write access to clothing/armor components.
/// </summary>
public abstract class SharedModsuitSystem : EntitySystem
{
    [Dependency]
    protected readonly IPrototypeManager Prototypes = default!;

    [Dependency]
    protected readonly SharedActionsSystem Actions = default!;

    public void ApplyArmor(EntityUid part, ModsuitThemePrototype theme)
    {
        if (theme.Armor is { } armorId)
        {
            var armor = EnsureComp<ArmorComponent>(part);
            armor.Modifiers = Prototypes.Index(armorId);
        }
        else
        {
            RemComp<ArmorComponent>(part);
        }
    }

    public void AttachMagboots(EntityUid boots)
    {
        EnsureComp<ItemToggleComponent>(boots);
        EnsureComp<MagbootsComponent>(boots);

        var toggle = EnsureComp<ToggleClothingComponent>(boots);
        toggle.Action = "ActionToggleMagboots";
        Actions.AddAction(boots, ref toggle.ActionEntity, toggle.Action, boots);
    }

    public void ApplyPartVisuals(
        EntityUid part,
        ModsuitPartSlot slot,
        ModsuitThemePrototype theme,
        string skin,
        bool isSealed)
    {
        if (!TryComp<ClothingComponent>(part, out var clothing))
            return;

        if (!theme.Skins.TryGetValue(skin, out var skinData)
            || !skinData.Parts.TryGetValue(GetPartKey(slot), out var partSkin))
            return;

        var state = isSealed ? partSkin.SealedState ?? partSkin.State : partSkin.State;
        if (state != null)
            clothing.EquippedState = state;

        Dirty(part, clothing);
    }

    private static string GetPartKey(ModsuitPartSlot slot)
    {
        return slot switch
        {
            ModsuitPartSlot.Helmet => "helmet",
            ModsuitPartSlot.Chestplate => "chestplate",
            ModsuitPartSlot.Gauntlets => "gauntlets",
            ModsuitPartSlot.Boots => "boots",
            _ => "helmet",
        };
    }
}
