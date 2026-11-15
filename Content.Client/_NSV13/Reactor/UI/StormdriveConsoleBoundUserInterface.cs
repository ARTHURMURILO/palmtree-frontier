// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._NSV13.Reactor;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._NSV13.Reactor.UI;

/// <summary>Client side of the stormdrive control console.</summary>
[UsedImplicitly]
public sealed class StormdriveConsoleBoundUserInterface : BoundUserInterface
{
    private StormdriveConsoleWindow? _window;

    public StormdriveConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<StormdriveConsoleWindow>();
        _window.OnSetRods += percent => SendMessage(new StormdriveConsoleSetRodsMessage(percent));
        _window.OnPreset += preset => SendMessage(new StormdriveConsolePresetMessage(preset));
        _window.OnToggleMaintenance += () => SendMessage(new StormdriveConsoleMaintenanceMessage());
        _window.OnToggleDump += () => SendMessage(new StormdriveConsoleDumpMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is StormdriveConsoleBoundUserInterfaceState telemetry)
            _window?.UpdateState(telemetry);
    }
}
