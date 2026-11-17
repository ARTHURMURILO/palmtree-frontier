using System;
using Content.Shared._PS.Circuitry;
using Robust.Client.UserInterface;

namespace Content.Client._PS.Circuitry.UI;

public sealed class CircuitCpuBoundUserInterface : BoundUserInterface
{
    private CircuitCpuWindow? _window;

    public CircuitCpuBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CircuitCpuWindow>();
        _window.OnSave += source => SendMessage(new CircuitCpuSaveMessage(source));
        _window.OnRun += source => SendMessage(new CircuitCpuRunMessage(source));
        _window.OnStop += () => SendMessage(new CircuitCpuStopMessage());
        _window.OnClearLog += () => SendMessage(new CircuitCpuClearLogMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is CircuitCpuState cpuState)
            _window?.Populate(cpuState);
    }
}
