using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// Adds storage to the MODsuit control unit while installed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ModsuitStorageModuleComponent : Component
{
    [DataField]
    public List<Box2i> Grid = new()
    {
        new(0, 0, 3, 1),
    };
}
