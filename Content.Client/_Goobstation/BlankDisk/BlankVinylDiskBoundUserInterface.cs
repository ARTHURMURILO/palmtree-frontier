using System.Linq;
using Content.Shared._Goobstation.BlankDisk;
using Content.Shared._Goobstation.StationRadio.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Goobstation.BlankDisk;

public sealed class BlankVinylDiskBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private BlankVinylDiskWindow? _window;

    public BlankVinylDiskBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        var options = _proto.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract
                        && p.TryGetComponent<VinylComponent>("Vinyl", out var vinyl)
                        && vinyl.Song != null)
            .OrderBy(p => p.Name)
            .Select(p => (p.ID, p.Name))
            .ToList();

        _window = new BlankVinylDiskWindow(options);
        _window.OnSelected += id =>
        {
            SendMessage(new BlankVinylDiskSelectMessage(id));
            Close();
        };
        _window.OnClose += Close;
        _window.OpenCentered();
    }
}
