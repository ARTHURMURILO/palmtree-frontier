using System.Linq;
using Content.Shared._Goobstation.BlankDisk;
using Content.Shared.Audio.Jukebox;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Goobstation.BlankDisk;

public sealed class BlankVinylDiskBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IEntitySystemManager _systems = default!;

    private BlankVinylDiskWindow? _window;

    public BlankVinylDiskBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        // The server can re-open a BUI that is already open (container changes, state resync).
        // Creating another window every time is how you end up with a pile of them.
        if (IsOpened)
            return;

        base.Open();

        var sprite = _systems.GetEntitySystem<SpriteSystem>();

        var options = _proto.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract
                        && p.TryGetComponent<JukeboxContainerComponent>("JukeboxContainer", out var container)
                        && container.Tracks.Count > 0)
            .OrderBy(p => p.Name)
            .Select(p => new BlankVinylDiskOption(p.ID, DisplayName(p.Name), sprite.Frame0(p)))
            .ToList();

        var window = new BlankVinylDiskWindow(options);
        _window = window;

        window.OnSelected += id =>
        {
            SendMessage(new BlankVinylDiskSelectMessage(id));
            Close();
        };
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;

            Close();
        };

        window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        // The server closed the UI (dropped disk, disk transformed, entity deleted, ...).
        // Close the window here, otherwise it stays on screen forever and the next
        // interaction opens a second one next to it.
        var window = _window;
        _window = null;

        if (window is { IsOpen: true })
            window.Close();
    }

    /// <summary>
    /// Records are named "vinyl disk (Song Title)" in prototypes; the window only needs the title,
    /// the icon already shows that it is a disk.
    /// </summary>
    private static string DisplayName(string name)
    {
        const string prefix = "vinyl disk (";
        if (name.StartsWith(prefix) && name.EndsWith(")"))
            return name[prefix.Length..^1];

        return name;
    }
}
