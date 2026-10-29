using Content.Client._PS.ShipCombat.UI;
using Content.Shared._PS.ShipCombat;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Map;

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

        _window.OnConditionConfirmed += condition => SendMessage(new ShipConsoleSetConditionMessage(condition));
        _window.OnAutomaticConditionChanged += enabled => SendMessage(new TacticalConsoleSetAutomaticConditionMessage(enabled));
        _window.OnAutomaticFireChanged += enabled => SendMessage(new TacticalConsoleSetAutomaticFireMessage(enabled));
        _window.OnAutomaticRetaliationChanged += enabled => SendMessage(new TacticalConsoleSetAutomaticRetaliationMessage(enabled));
        _window.OnWeaponModeChanged += mode => SendMessage(new TacticalConsoleSetWeaponModeMessage(mode));
        _window.OnTargetModeChanged += mode => SendMessage(new TacticalConsoleSetTargetModeMessage(mode));
        _window.OnClearLockRequested += () => SendMessage(new TacticalConsoleClearLockMessage());
        _window.OnManualFireRequested += () => SendMessage(new TacticalConsoleManualFireMessage());
        _window.OnFireTorpedoesRequested += () => SendMessage(new TacticalConsoleFireTorpedoesMessage());
        _window.OnSilentChanged += silent => SendMessage(new TacticalConsoleSetSilentMessage(silent));

        // Palmtree: clicking a contact on the radar asks the server to lock it.
        _window.OnLockRequested += coords =>
        {
            var mapManager = IoCManager.Resolve<IMapManager>();
            var mapCoords = EntMan.System<SharedTransformSystem>().ToMapCoordinates(coords);

            if (!mapManager.TryFindGridAt(mapCoords, out var gridUid, out _))
                return;

            SendMessage(new TacticalConsoleLockRequestMessage(EntMan.GetNetEntity(gridUid)));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is TacticalConsoleBoundUserInterfaceState consoleState)
            _window?.UpdateState(consoleState);
    }
}
