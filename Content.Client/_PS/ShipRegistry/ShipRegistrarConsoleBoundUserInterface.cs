using Content.Shared._PS.ShipRegistry;
using Content.Shared.Containers.ItemSlots;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._PS.ShipRegistry;

/// <summary>
/// Client side of the Ship Registrar console.
/// </summary>
[UsedImplicitly]
public sealed class ShipRegistrarConsoleBoundUserInterface : BoundUserInterface
{
    private ShipRegistrarConsoleMenu? _menu;

    public ShipRegistrarConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<ShipRegistrarConsoleMenu>();

        _menu.OnPrivilegedId += () =>
            SendMessage(new ItemSlotButtonPressedEvent(ShipRegistrarConsoleComponent.PrivilegedIdCardSlotId));
        _menu.OnTargetId += () =>
            SendMessage(new ItemSlotButtonPressedEvent(ShipRegistrarConsoleComponent.TargetIdCardSlotId));
        _menu.OnRegister += (grid, name, suffix) => SendMessage(new ShipRegistrarRegisterMessage(grid, name, suffix));
        _menu.OnPreview += grid => SendMessage(new ShipRegistrarPreviewMessage(grid));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ShipRegistrarUiState registrarState)
            _menu?.UpdateState(registrarState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _menu?.Dispose();
        _menu = null;
    }
}
