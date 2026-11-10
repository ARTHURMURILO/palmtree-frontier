using Content.Shared._PS.Atmos.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.IdentityManagement;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._PS.Atmos.UI;

/// <summary>
/// Palmtree: opens and updates the <see cref="GasHeatPumpWindow"/>.
/// </summary>
[UsedImplicitly]
public sealed class GasHeatPumpBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private GasHeatPumpWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<GasHeatPumpWindow>();
        _window.SetEntity(Owner);

        _window.ToggleStatusButtonPressed += OnTogglePressed;
        _window.TargetTemperatureChanged += OnTargetTemperatureChanged;
        _window.SafetiesChanged += OnSafetiesChanged;

        // The input field is only filled in on open; the server dirties the readings once a second and
        // rewriting the spin box from Update() would eat whatever the player is typing.
        if (EntMan.TryGetComponent(Owner, out GasHeatPumpComponent? pump))
        {
            _window.MinTemperature = pump.MinTemperature;
            _window.MaxTemperature = pump.MaxTemperature;
            _window.SetTargetTemperature(pump.TargetTemperature);
        }

        Update();
    }

    public override void Update()
    {
        if (_window == null)
            return;

        _window.Title = Identity.Name(Owner, EntMan);

        if (!EntMan.TryGetComponent(Owner, out GasHeatPumpComponent? pump))
            return;

        _window.MinTemperature = pump.MinTemperature;
        _window.MaxTemperature = pump.MaxTemperature;
        var powered = EntMan.System<SharedPowerReceiverSystem>().IsPowered(Owner);

        _window.UpdateStatusButton(pump.Enabled);
        _window.SetPowered(powered);
        _window.SetSafeties(pump.Safeties);
        _window.UpdateInfo(
            pump.Cooling,
            pump.GasTemperature,
            pump.GasPressure,
            pump.WasteTemperature,
            pump.WastePressure,
            pump.Efficiency,
            pump.HeatMoved);
    }

    private void OnTogglePressed()
    {
        if (_window == null)
            return;

        SendPredictedMessage(new GasHeatPumpToggleMessage(_window.PumpEnabled));
    }

    private void OnTargetTemperatureChanged(float temperature)
    {
        SendPredictedMessage(new GasHeatPumpChangeTemperatureMessage(temperature));
    }

    private void OnSafetiesChanged(bool safeties)
    {
        SendPredictedMessage(new GasHeatPumpToggleSafetiesMessage(safeties));
    }
}
