using Content.Shared.Actions;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// Performs the control unit's deploy/retract action.
/// </summary>
public sealed partial class ModsuitToggleEvent : InstantActionEvent;

/// <summary>
/// Toggles the sealed state of an active MODsuit.
/// </summary>
public sealed partial class ModsuitSealEvent : InstantActionEvent;

/// <summary>
/// Opens the MODsuit control panel.
/// </summary>
public sealed partial class ModsuitUiEvent : InstantActionEvent;

/// <summary>
/// Raised after a MODsuit control unit has created its core and parts so inbuilt modules can be installed.
/// </summary>
[ByRefEvent]
public record struct ModsuitModulesInitEvent;
