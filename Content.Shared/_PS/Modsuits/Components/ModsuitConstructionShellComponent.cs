using Robust.Shared.GameStates;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// Tracks the assembly state of a MODsuit shell.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ModsuitConstructionShellComponent : Component
{
    [DataField]
    public bool HasCore;

    [DataField]
    public bool CoreSecured;

    [DataField]
    public bool Wrenched;

    [DataField]
    public bool Secured;

    [DataField]
    public HashSet<ModsuitPartSlot> Parts = new();
}
