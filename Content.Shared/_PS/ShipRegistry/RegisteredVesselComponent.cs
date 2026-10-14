using Content.Shared._NF.Shipyard;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// Palmtree: marks a grid as a player-registered civilian vessel (created by the Ship Registrar
/// console). It gives the grid the vessel identity the Bluespace Dock requires without needing a
/// shipyard vessel prototype - registered ships count as civilian.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RegisteredVesselComponent : Component
{
    /// <summary>
    /// Listing group the registered ship belongs to. Only the civilian groups are accepted by the
    /// Bluespace Dock.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ShipyardConsoleUiKey Group = ShipyardConsoleUiKey.Shipyard;

    /// <summary>
    /// Model shown in the Bluespace Dock list ("Custom" for hand-built ships).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Model = "Custom";
}
