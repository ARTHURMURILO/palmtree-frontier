using Content.Shared._PS.Protean;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._PS.Protean;

public sealed class ProteanBoundUserInterface : BoundUserInterface
{
    private ProteanWindow? _window;

    public ProteanBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ProteanWindow>();
        _window.FoldPressed += () => SendMessage(new ProteanActionMessage(ProteanUiAction.ToggleFold));
        _window.LockPressed += () => SendMessage(new ProteanActionMessage(ProteanUiAction.ToggleLock));
        _window.EjectPressed += () => SendMessage(new ProteanActionMessage(ProteanUiAction.EjectSuit));
        _window.ServoPressed += () => SendMessage(new ProteanActionMessage(ProteanUiAction.ServoSpeed));
        _window.LowPowerPressed += () => SendMessage(new ProteanActionMessage(ProteanUiAction.ToggleLowPower));
        _window.HealPressed += () => SendMessage(new ProteanActionMessage(ProteanUiAction.Heal));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ProteanUiState proteanState)
            _window?.UpdateState(proteanState);
    }
}
