using Robust.Shared.GameStates;

namespace Content.Server._PS.CartridgeLoader.Cartridges;

/// <summary>
/// Palmtree: server-side state for the RIX cartridge.
/// </summary>
[RegisterComponent, Access(typeof(PSBankTransferCartridgeSystem))]
public sealed partial class PSBankTransferCartridgeComponent : Component
{
    [DataField]
    public string Result = string.Empty;

    [DataField]
    public bool Success;

    [DataField]
    public TimeSpan NextTransfer;
}
