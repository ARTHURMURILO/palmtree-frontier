using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// Base data for a MODsuit module. Behaviour lives in separate components/systems so themes and modules stay data-driven.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModsuitModuleComponent : Component
{
    [DataField, AutoNetworkedField]
    public ModsuitModuleType ModuleType = ModsuitModuleType.Passive;

    [DataField, AutoNetworkedField]
    public int Complexity = 1;

    /// <summary>
    /// Power consumed when a usable/active module fires.
    /// </summary>
    [DataField]
    public float UsePower;

    /// <summary>
    /// Power consumed per second while a toggle module is active.
    /// </summary>
    [DataField]
    public float ActivePower;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField]
    public List<EntProtoId> IncompatibleModules = new();
}
