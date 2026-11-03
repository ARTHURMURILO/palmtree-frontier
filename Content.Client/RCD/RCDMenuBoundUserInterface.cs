using Content.Client.Popups;
using Content.Client.UserInterface.Controls;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Collections;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.RCD;

[UsedImplicitly]
public sealed class RCDMenuBoundUserInterface : BoundUserInterface
{
    private const string TopLevelActionCategory = "Main";

    private static readonly Dictionary<string, (string Tooltip, SpriteSpecifier Sprite)> PrototypesGroupingInfo
        = new Dictionary<string, (string Tooltip, SpriteSpecifier Sprite)>
        {
            ["WallsAndFlooring"] = ("rcd-component-walls-and-flooring", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/walls_and_flooring.png"))),
            ["WindowsAndGrilles"] = ("rcd-component-windows-and-grilles", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/windows_and_grilles.png"))),
            ["Airlocks"] = ("rcd-component-airlocks", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/airlocks.png"))),
            ["Doors"] = ("rcd-component-doors", new SpriteSpecifier.Rsi(new ResPath("Structures/Doors/Shutters/blastdoor.rsi"), "closed")),
            ["Lighting"] = ("rcd-component-lighting", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/lighting.png"))),
            // Palmtree: nested lamp/bulb light categories
            ["LightingLamps"] = ("rcd-component-lighting-lamps", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/tube_light.png"))),
            ["LightingBulbs"] = ("rcd-component-lighting-bulbs", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/bulb_light.png"))),
            // Palmtree: ship construction suite
            ["Storage"] = ("rcd-component-storage", new SpriteSpecifier.Rsi(new ResPath("Structures/Storage/closet.rsi"), "generic")),
            ["Power"] = ("rcd-component-power", new SpriteSpecifier.Rsi(new ResPath("Structures/Power/substation.rsi"), "substation")),
            ["PowerMachinery"] = ("rcd-component-power-machinery", new SpriteSpecifier.Rsi(new ResPath("Structures/Power/substation.rsi"), "substation")),
            ["PowerWiring"] = ("rcd-component-power-wiring", new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Radial/RCD/multicoil.png"))),
            ["Thrusters"] = ("rcd-component-thrusters", new SpriteSpecifier.Rsi(new ResPath("_NF/Structures/Shuttles/thruster.rsi"), "base")),
            ["Gyroscopes"] = ("rcd-component-gyroscopes", new SpriteSpecifier.Rsi(new ResPath("_NF/Structures/Shuttles/gyroscope.rsi"), "base")),
            // Frontier - Pirate RCD
            // Palmtree: PirateWallsAndFlooring was merged into WallsAndFlooring; its modes now use
            // the standard category so the menu no longer shows a second "Walls and flooring".
            ["PirateWindowsAndGrilles"] = ("rcd-component-pirate-windows-and-grilles", new SpriteSpecifier.Texture(new ResPath("/Textures/_NF/Interface/Radial/RCD/windows_and_grilles.png"))),
            ["PirateAirlocks"] = ("rcd-component-pirate-airlocks", new SpriteSpecifier.Texture(new ResPath("/Textures/_NF/Interface/Radial/RCD/airlocks.png"))),
            // End Frontier
            // Palmtree - RPD
            ["Piping"] = ("rcd-component-piping", new SpriteSpecifier.Texture(new ResPath("/Textures/_PS/Interface/Radial/RPD/fourway.png"))),
            ["AtmosphericUtility"] = ("rcd-component-atmosphericutility", new SpriteSpecifier.Texture(new ResPath("/Textures/_PS/Interface/Radial/RPD/port.png"))),
            ["PumpsValves"] = ("rcd-component-pumps", new SpriteSpecifier.Texture(new ResPath("/Textures/_PS/Interface/Radial/RPD/pump_volume.png"))),
            ["Vents"] = ("rcd-component-vents", new SpriteSpecifier.Texture(new ResPath("/Textures/_PS/Interface/Radial/RPD/vent_passive.png"))),
            // End Palmtree
        };

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;

    private SimpleRadialMenu? _menu;

    public RCDMenuBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent<RCDComponent>(Owner, out var rcd))
            return;

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.Track(Owner);
        var models = ConvertToButtons(rcd.AvailablePrototypes);
        _menu.SetButtons(models);

        _menu.OpenOverMouseScreenPosition();
    }

    private IEnumerable<RadialMenuOptionBase> ConvertToButtons(HashSet<ProtoId<RCDPrototype>> prototypes)
    {
        Dictionary<string, List<RadialMenuActionOptionBase>> buttonsByCategory = new();
        ValueList<RadialMenuActionOptionBase> topLevelActions = new();
        foreach (var protoId in prototypes)
        {
            var prototype = _prototypeManager.Index(protoId);
            if (prototype.Category == TopLevelActionCategory)
            {
                var topLevelActionOption = new RadialMenuActionOption<RCDPrototype>(HandleMenuOptionClick, prototype)
                {
                    IconSpecifier = RadialMenuIconSpecifier.With(prototype.Sprite),
                    ToolTip = GetTooltip(prototype)
                };
                topLevelActions.Add(topLevelActionOption);
                continue;
            }

            if (!PrototypesGroupingInfo.TryGetValue(prototype.Category, out _))
                continue;

            if (!buttonsByCategory.TryGetValue(prototype.Category, out var list))
            {
                list = new List<RadialMenuActionOptionBase>();
                buttonsByCategory.Add(prototype.Category, list);
            }

            var actionOption = new RadialMenuActionOption<RCDPrototype>(HandleMenuOptionClick, prototype)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(prototype.Sprite),
                ToolTip = GetTooltip(prototype)
            };
            list.Add(actionOption);
        }

        // Palmtree: categories that contain nested sub-categories. Direct modes of the parent are
        // listed alongside the sub-category buttons (Doors has blast doors/shutters plus Airlocks).
        var nestedCategories = new Dictionary<string, string[]>
        {
            ["Lighting"] = new[] { "LightingLamps", "LightingBulbs" },
            ["Power"] = new[] { "PowerMachinery", "PowerWiring" },
            ["Doors"] = new[] { "Airlocks" },
        };

        var models = new List<RadialMenuOptionBase>();
        var handled = new HashSet<string>();

        foreach (var (parent, subCategories) in nestedCategories)
        {
            var nested = new List<RadialMenuOptionBase>();

            if (buttonsByCategory.TryGetValue(parent, out var directList))
                nested.AddRange(directList);

            foreach (var category in subCategories)
            {
                if (!buttonsByCategory.TryGetValue(category, out var subList) || subList.Count == 0)
                    continue;

                var subInfo = PrototypesGroupingInfo[category];
                nested.Add(new RadialMenuNestedLayerOption(subList)
                {
                    IconSpecifier = RadialMenuIconSpecifier.With(subInfo.Sprite),
                    ToolTip = Loc.GetString(subInfo.Tooltip)
                });

                handled.Add(category);
            }

            if (nested.Count == 0)
                continue;

            var parentInfo = PrototypesGroupingInfo[parent];
            models.Add(new RadialMenuNestedLayerOption(nested)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(parentInfo.Sprite),
                ToolTip = Loc.GetString(parentInfo.Tooltip)
            });

            handled.Add(parent);
        }

        foreach (var (key, list) in buttonsByCategory)
        {
            if (handled.Contains(key))
                continue;

            var groupInfo = PrototypesGroupingInfo[key];
            models.Add(new RadialMenuNestedLayerOption(list)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(groupInfo.Sprite),
                ToolTip = Loc.GetString(groupInfo.Tooltip)
            });
        }

        foreach (var action in topLevelActions)
        {
            models.Add(action);
        }

        return models;
    }

    private void HandleMenuOptionClick(RCDPrototype proto)
    {
        // A predicted message cannot be used here as the RCD UI is closed immediately
        // after this message is sent, which will stop the server from receiving it
        SendMessage(new RCDSystemMessage(proto.ID));


        if (_playerManager.LocalSession?.AttachedEntity == null)
            return;

        var selectedName = GetModeName(proto);
        var msg = Loc.GetString("rcd-component-change-mode", ("mode", selectedName));

        if (proto.Mode is RcdMode.ConstructTile or RcdMode.ConstructObject)
            msg = Loc.GetString("rcd-component-change-build-mode", ("name", selectedName));

        // Popup message
        var popup = EntMan.System<PopupSystem>();
        popup.PopupClient(msg, Owner, _playerManager.LocalSession.AttachedEntity);
    }

    private string GetModeName(RCDPrototype proto)
    {
        // Palmtree: an explicit mode name wins over the entity name, so light variants can say
        // "cyan tube light" instead of every button being just "light".
        if (proto.SetName != "Unknown")
            return Loc.GetString(proto.SetName);

        if (proto.Mode is RcdMode.ConstructTile or RcdMode.ConstructObject
            && proto.Prototype != null
            && _prototypeManager.Resolve(proto.Prototype, out var entProto)) // don't use Resolve because this can be a tile
        {
            return Loc.GetString(entProto.Name);
        }

        return Loc.GetString(proto.SetName);
    }

    private string GetTooltip(RCDPrototype proto)
    {
        var tooltip = GetModeName(proto);
        tooltip = OopsConcat(char.ToUpper(tooltip[0]).ToString(), tooltip.Remove(0, 1));

        return tooltip;
    }

    private static string OopsConcat(string a, string b)
    {
        // This exists to prevent Roslyn being clever and compiling something that fails sandbox checks.
        return a + b;
    }
}
