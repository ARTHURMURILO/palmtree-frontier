using Content.Shared._PS.Atmos.Components;
using Content.Shared.IdentityManagement;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._PS.Atmos.UI;

/// <summary>
/// Palmtree: opens and updates the <see cref="GasTemperatureGateWindow"/>.
/// </summary>
[UsedImplicitly]
public sealed class GasTemperatureGateBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private GasTemperatureGateWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<GasTemperatureGateWindow>();
        _window.SetEntity(Owner);

        _window.ToggleStatusButtonPressed += OnTogglePressed;
        _window.TargetTemperatureChanged += OnTargetTemperatureChanged;
        _window.InvertedChanged += OnInvertedChanged;

        Update();
    }

    public override void Update()
    {
        if (_window == null)
            return;

        _window.Title = Identity.Name(Owner, EntMan);

        if (!EntMan.TryGetComponent(Owner, out GasTemperatureGateComponent? gate))
            return;

        _window.MaxTemperature = gate.MaxTemperature;
        _window.UpdateStatusButton(gate.Enabled);
        _window.SetTargetTemperature(gate.TargetTemperature);
        _window.SetInverted(gate.Inverted);
        _window.UpdateInfo(gate.InletTemperature, gate.OutletTemperature);
    }

    private void OnTogglePressed()
    {
        if (_window == null)
            return;

        SendPredictedMessage(new GasTemperatureGateToggleMessage(_window.GateEnabled));
    }

    private void OnTargetTemperatureChanged(float temperature)
    {
        SendPredictedMessage(new GasTemperatureGateChangeTemperatureMessage(temperature));
    }

    private void OnInvertedChanged(bool inverted)
    {
        SendPredictedMessage(new GasTemperatureGateInvertMessage(inverted));
    }
}
