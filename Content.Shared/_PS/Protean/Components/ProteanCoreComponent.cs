using Robust.Shared.GameStates;

namespace Content.Shared._PS.Protean;

/// <summary>
/// Marks a MODsuit core as the Protean refactory-linked core. Its charge is mirrored from the
/// owner's refactory metal store instead of being consumed as a normal battery.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProteanCoreComponent : Component;
