using Content.Shared._PS.BluespaceDock;
using Content.Shared.Containers.ItemSlots;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._PS.BluespaceDock;

/// <summary>
/// Client side of the Bluespace Dock console. The window is bound to the console entity and the
/// server pushes the list of the inserted ID's stored ships.
/// </summary>
[UsedImplicitly]
public sealed class BluespaceDockConsoleBoundUserInterface : BoundUserInterface
{
    private BluespaceDockConsoleMenu? _menu;

    public BluespaceDockConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<BluespaceDockConsoleMenu>();

        _menu.OnDockCurrent += () => SendMessage(new BluespaceDockStoreMessage());
        _menu.OnSellActive += () => SendMessage(new BluespaceDockSellActiveMessage());
        _menu.OnUndock += shipId => SendMessage(new BluespaceDockRetrieveMessage(shipId));
        _menu.OnSellStored += shipId => SendMessage(new BluespaceDockSellStoredMessage(shipId));
        _menu.TargetIdButton.OnPressed += _ =>
            SendMessage(new ItemSlotButtonPressedEvent(BluespaceDockConsoleComponent.TargetIdCardSlotId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is BluespaceDockUiState dockState)
            _menu?.UpdateState(dockState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _menu?.Dispose();
        _menu = null;
    }
}
