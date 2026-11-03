using Content.Client._PS.ShipCombat.UI;
using Content.Shared._PS.ShipCombat;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._PS.ShipCombat.BUI;

[UsedImplicitly]
public sealed class ShieldingConsoleBoundUserInterface : BoundUserInterface
{
    private ShieldingConsoleWindow? _window;

    public ShieldingConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ShieldingConsoleWindow>();
        _window.SetConsole(Owner);

        _window.OnConditionConfirmed += condition => SendMessage(new ShipConsoleSetConditionMessage(condition));
        _window.OnPoolChanged += (pool, value) => SendMessage(new ShieldingConsoleSetPoolMessage(pool, value));
        _window.OnPoolLockChanged += (pool, locked) => SendMessage(new ShieldingConsoleSetPoolLockMessage(pool, locked));
        _window.OnPresetSelected += preset => SendMessage(new ShieldingConsoleSetPresetMessage(preset));
        _window.OnRedistribute += facing => SendMessage(new ShieldingConsoleRedistributeMessage(facing));
        _window.OnSilentChanged += silent => SendMessage(new ShieldingConsoleSetSilentMessage(silent));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ShieldingConsoleBoundUserInterfaceState consoleState)
            _window?.UpdateState(consoleState);
    }
}
