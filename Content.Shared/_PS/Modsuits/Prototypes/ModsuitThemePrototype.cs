using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// Data-driven MODsuit theme. Themes define a suit's protection, capacity and visual skins.
/// </summary>
[Prototype("modsuitTheme")]
public sealed partial class ModsuitThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string Name = "MODsuit";

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public string DefaultSkin = "standard";

    [DataField]
    public int ComplexityMax = 15;

    [DataField]
    public float ChargeDrain = 0.05f;

    [DataField]
    public float DeployedSlowdown = 0.75f;

    [DataField]
    public ProtoId<DamageModifierSetPrototype>? Armor;

    [DataField]
    public float HighPressureMultiplier = 0.1f;

    [DataField]
    public float LowPressureMultiplier = 1000f;

    [DataField]
    public float HeatingCoefficient = 0.01f;

    [DataField]
    public float CoolingCoefficient = 0.01f;

    [DataField]
    public List<EntProtoId> InbuiltModules = new();

    [DataField]
    public Dictionary<string, ModsuitSkin> Skins = new();
}

[DataDefinition]
public sealed partial class ModsuitSkin
{
    /// <summary>
    /// Keyed by lowercase part name: helmet, chestplate, gauntlets, boots.
    /// </summary>
    [DataField]
    public Dictionary<string, ModsuitPartSkin> Parts = new();
}

[DataDefinition]
public sealed partial class ModsuitPartSkin
{
    [DataField]
    public ResPath? Sprite;

    [DataField]
    public string? State;

    [DataField]
    public string? SealedState;

    [DataField]
    public string? OpenState;
}
