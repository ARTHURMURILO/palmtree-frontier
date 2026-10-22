using Content.Shared._PS.Modsuits;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.GameObjects;

namespace Content.Client._PS.Modsuits;

public sealed class ModsuitBoundUserInterface : BoundUserInterface
{
    private ModsuitWindow? _window;

    public ModsuitBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ModsuitWindow>();
        _window.TogglePressed += () => SendMessage(new ModsuitToggleMessage());
        _window.SealPressed += () => SendMessage(new ModsuitSealMessage());
        _window.RemovePressed += index => SendMessage(new ModsuitRemoveModuleMessage(index));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ModsuitUiState modsuitState)
            _window?.UpdateState(modsuitState);
    }
}
