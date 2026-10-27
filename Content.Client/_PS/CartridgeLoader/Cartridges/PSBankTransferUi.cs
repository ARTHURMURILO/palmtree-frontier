using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared._PS.CartridgeLoader.Cartridges;
using Robust.Client.UserInterface;

namespace Content.Client._PS.CartridgeLoader.Cartridges;

public sealed partial class PSBankTransferUi : UIFragment
{
    private PSBankTransferUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new PSBankTransferUiFragment();
        _fragment.OnTransfer += (target, amount) =>
        {
            var message = new CartridgeUiMessage(new PSBankTransferUiMessageEvent(target, amount));
            userInterface.SendMessage(message);
        };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PSBankTransferUiState cast)
            _fragment?.UpdateState(cast);
    }
}
