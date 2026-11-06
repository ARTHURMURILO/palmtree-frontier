// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._NSV13.Reactor;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._NSV13.Reactor.UI;

/// <summary>Client side of the stormdrive rod bay.</summary>
[UsedImplicitly]
public sealed class StormdriveRodsBoundUserInterface : BoundUserInterface
{
    private StormdriveRodsWindow? _window;

    public StormdriveRodsBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<StormdriveRodsWindow>();
        _window.OnRemoveRod += index => SendMessage(new StormdriveRemoveRodMessage(index));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is StormdriveRodsBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}
