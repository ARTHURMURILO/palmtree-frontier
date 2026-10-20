using Robust.Shared.GameStates;

namespace Content.Shared._PS.Protean;

/// <summary>
/// Marks a MODsuit module as the Protean servo module. Its modes are exposed through the Protean UI.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanServoModuleComponent : Component;
