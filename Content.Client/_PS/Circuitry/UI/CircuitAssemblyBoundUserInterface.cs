using Content.Shared._PS.Circuitry;
using Robust.Client.UserInterface;

namespace Content.Client._PS.Circuitry.UI;

public sealed class CircuitAssemblyBoundUserInterface : BoundUserInterface
{
    private CircuitAssemblyWindow? _window;

    public CircuitAssemblyBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CircuitAssemblyWindow>();
        _window.OnEject += slotId => SendMessage(new CircuitAssemblyEjectMessage(slotId));
        _window.OnRename += name => SendMessage(new CircuitAssemblyRenameMessage(name));
        _window.OnLock += locked => SendMessage(new CircuitAssemblyLockMessage(locked));
        _window.OnTransferOwner += owner => SendMessage(new CircuitAssemblyTransferOwnerMessage(owner));
        _window.OnOpenProgram += () => SendMessage(new CircuitAssemblyOpenCpuMessage());
        _window.OnRun += () => SendMessage(new CircuitAssemblyRunMessage());
        _window.OnStop += () => SendMessage(new CircuitAssemblyStopMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is CircuitAssemblyState assemblyState)
            _window?.Populate(assemblyState);
    }
}
