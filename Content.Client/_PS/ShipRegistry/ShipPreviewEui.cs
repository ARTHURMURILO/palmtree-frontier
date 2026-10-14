using Content.Client.Eui;
using Content.Shared._PS.ShipRegistry;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._PS.ShipRegistry;

/// <summary>
/// Client side of the ship preview EUI: a window with a live camera view of the ship.
/// </summary>
[UsedImplicitly]
public sealed partial class ShipPreviewEui : BaseEui
{
    private readonly ShipPreviewWindow _window;
    private readonly ShipPreviewControl _control;

    public ShipPreviewEui()
    {
        _window = new ShipPreviewWindow();
        _control = new ShipPreviewControl();

        _window.Contents.AddChild(_control);

        _window.OnClose += () => SendMessage(new CloseEuiMessage());
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
        if (baseState is not ShipPreviewEuiState state)
            return;

        _window.SetState(state);
        _control.SetState(state);
    }
}
