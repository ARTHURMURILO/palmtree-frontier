using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// The back-slot controller for a MODsuit. Parts, modules and the core live in internal containers while retracted.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModsuitControlComponent : Component
{
    public const string PartsContainerId = "modsuit_parts";
    public const string ModuleContainerId = "modsuit_modules";
    public const string OverslotContainerId = "modsuit_overslot";
    public const string CoreContainerId = "modsuit_core";

    [DataField, AutoNetworkedField]
    public ProtoId<ModsuitThemePrototype> Theme = "ModsuitThemeStandard";

    [DataField, AutoNetworkedField]
    public string Skin = "standard";

    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField]
    public bool Sealed;

    /// <summary>
    /// Whether the service hatch is open. The core and modules can only be removed while open and inactive.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Open;

    [DataField, AutoNetworkedField]
    public EntityUid? Wearer;

    [DataField, AutoNetworkedField]
    public EntityUid? Core;

    [DataField]
    public List<EntityUid> Modules = new();

    [DataField]
    public Dictionary<ModsuitPartSlot, EntityUid> Parts = new();

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? SealActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? UiActionEntity;

    [DataField]
    public EntProtoId ToggleAction = "ActionModsuitToggle";

    [DataField]
    public EntProtoId SealAction = "ActionModsuitSeal";

    [DataField]
    public EntProtoId UiAction = "ActionModsuitUi";

    [DataField]
    public EntProtoId HelmetPrototype = "ModsuitHelmet";

    [DataField]
    public EntProtoId ChestplatePrototype = "ModsuitChestplate";

    [DataField]
    public EntProtoId GauntletsPrototype = "ModsuitGauntlets";

    [DataField]
    public EntProtoId BootsPrototype = "ModsuitBoots";

    [DataField]
    public EntProtoId CorePrototype = "ModsuitCoreStandard";

    [ViewVariables]
    public Container PartsContainer = default!;

    [ViewVariables]
    public Container ModuleContainer = default!;

    [ViewVariables]
    public Container OverslotContainer = default!;

    [ViewVariables]
    public ContainerSlot CoreContainer = default!;
}
