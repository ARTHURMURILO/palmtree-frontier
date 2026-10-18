using Content.Client._PS.ShipCombat.UI;
using Content.Shared._PS.ShipCombat;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._PS.ShipCombat.BUI;

[UsedImplicitly]
public sealed class TacticalConsoleBoundUserInterface : BoundUserInterface
{
    private TacticalConsoleWindow? _window;

    public TacticalConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<TacticalConsoleWindow>();
        _window.SetConsole(Owner);

        _window.OnConditionConfirmed += condition => SendMessage(new TacticalConsoleSetConditionMessage(condition));
        _window.OnAutomaticConditionChanged += enabled => SendMessage(new TacticalConsoleSetAutomaticConditionMessage(enabled));
        _window.OnAutomaticFireChanged += enabled => SendMessage(new TacticalConsoleSetAutomaticFireMessage(enabled));
        _window.OnAutomaticRetaliationChanged += enabled => SendMessage(new TacticalConsoleSetAutomaticRetaliationMessage(enabled));
        _window.OnWeaponModeChanged += mode => SendMessage(new TacticalConsoleSetWeaponModeMessage(mode));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is TacticalConsoleBoundUserInterfaceState consoleState)
            _window?.UpdateState(consoleState);
    }
}
