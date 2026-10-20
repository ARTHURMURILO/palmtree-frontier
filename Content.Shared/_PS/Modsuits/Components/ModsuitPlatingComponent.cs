using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// Finishes a MODsuit shell and selects its theme.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ModsuitPlatingComponent : Component
{
    [DataField(required: true)]
    public ProtoId<ModsuitThemePrototype> Theme = "ModsuitThemeStandard";
}
