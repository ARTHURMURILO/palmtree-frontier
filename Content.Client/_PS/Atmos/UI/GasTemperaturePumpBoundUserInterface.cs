using Content.Shared._PS.Atmos.Components;
using Content.Shared.IdentityManagement;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._PS.Atmos.UI;

/// <summary>
/// Palmtree: opens and updates the <see cref="GasTemperaturePumpWindow"/>.
/// </summary>
[UsedImplicitly]
public sealed class GasTemperaturePumpBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private GasTemperaturePumpWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<GasTemperaturePumpWindow>();
        _window.SetEntity(Owner);

        _window.ToggleStatusButtonPressed += OnTogglePressed;
        _window.HeatTransferRateChanged += OnRateChanged;

        Update();
    }

    public override void Update()
    {
        if (_window == null)
            return;

        _window.Title = Identity.Name(Owner, EntMan);

        if (!EntMan.TryGetComponent(Owner, out GasTemperaturePumpComponent? pump))
            return;

        _window.MaxRate = pump.MaxHeatTransferRate;
        _window.UpdateStatusButton(pump.Enabled);
        _window.SetHeatTransferRate(pump.HeatTransferRate);
        _window.UpdateInfo(pump.InletTemperature, pump.OutletTemperature, pump.TransferredHeat);
    }

    private void OnTogglePressed()
    {
        if (_window == null)
            return;

        SendPredictedMessage(new GasTemperaturePumpToggleMessage(_window.PumpEnabled));
    }

    private void OnRateChanged(float rate)
    {
        SendPredictedMessage(new GasTemperaturePumpChangeRateMessage(rate));
    }
}
