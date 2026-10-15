using Content.Client.Eui;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._PS.BluespaceDock;

/// <summary>
/// Client side of the admin ship manager.
/// </summary>
[UsedImplicitly]
public sealed partial class ManageShipsEui : BaseEui
{
    private readonly ManageShipsWindow _window;

    public ManageShipsEui()
    {
        _window = new ManageShipsWindow();

        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnPreview += grid => SendMessage(new ManageShipsPreviewMessage(grid));
        _window.OnDelete += grid => SendMessage(new ManageShipsDeleteMessage(grid));
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase baseState)
    {
        if (baseState is ManageShipsEuiState state)
            _window.UpdateState(state);
    }
}
