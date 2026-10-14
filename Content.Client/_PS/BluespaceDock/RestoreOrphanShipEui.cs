using Content.Client.Eui;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._PS.BluespaceDock;

/// <summary>
/// Client side of the orphaned ship admin tool.
/// </summary>
[UsedImplicitly]
public sealed partial class RestoreOrphanShipEui : BaseEui
{
    private readonly RestoreOrphanShipWindow _window;

    public RestoreOrphanShipEui()
    {
        _window = new RestoreOrphanShipWindow();

        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnPreview += file => SendMessage(new RestoreOrphanShipPreviewMessage(file));
        _window.OnDelete += file => SendMessage(new RestoreOrphanShipDeleteMessage(file));
        _window.OnRestore += (file, userId, shipName, suffix) =>
            SendMessage(new RestoreOrphanShipRestoreMessage(file, userId, shipName, suffix));
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
        if (baseState is RestoreOrphanShipEuiState state)
            _window.UpdateState(state);
    }
}
