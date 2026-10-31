// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._NSV13.Reactor;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._NSV13.Reactor.UI;

/// <summary>Client side of the RBMK control console.</summary>
[UsedImplicitly]
public sealed class RbmkConsoleBoundUserInterface : BoundUserInterface
{
    private RbmkConsoleWindow? _window;

    public RbmkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<RbmkConsoleWindow>();
        _window.OnSetTarget += target => SendMessage(new RbmkConsoleSetTargetMessage(target));
        _window.OnEjectRod += index => SendMessage(new RbmkConsoleEjectRodMessage(index));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is RbmkConsoleBoundUserInterfaceState telemetry)
            _window?.UpdateState(telemetry);
    }
}
