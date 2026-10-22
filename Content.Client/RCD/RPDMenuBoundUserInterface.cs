using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.RCD;

/// <summary>
/// Opens the RPD's own menu, which is the mode radial plus the pipe colour strip.
/// </summary>
/// <remarks>
/// Palmtree: ported from the old Coyote code. The palette entries mirror the spray painter's
/// pipe colours so an RPD-tinted pipe matches what players expect from spray painting.
/// </remarks>
[UsedImplicitly]
public sealed class RPDMenuBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IClyde _displayManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private RPDMenu? _menu;

    public RPDMenuBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        if (!_entityManager.TryGetComponent<RCDComponent>(Owner, out var rcd))
            return;

        _menu = this.CreateWindow<RPDMenu>();
        _menu.SetEntity(Owner);
        _menu.ColorSelected += OnColorSelected;
        _menu.SendRCDSystemMessageAction += OnRCDSystemMessage;

        _menu.Populate(BuildPalette(rcd), rcd.PipeColorKey);

        var vpSize = _displayManager.ScreenSize;
        _menu.OpenCenteredAt(_inputManager.MouseScreenPosition.Position / vpSize);
    }

    /// <summary>
    /// Turns the RPD's colour palette into the menu's swatch list, with a leading "no paint" entry.
    /// </summary>
    private static Dictionary<string, Color?> BuildPalette(RCDComponent rcd)
    {
        var palette = new Dictionary<string, Color?>
        {
            { "default", null },
        };

        foreach (var (key, color) in rcd.PipeColorPalette)
        {
            palette[key] = color;
        }

        return palette;
    }

    private void OnColorSelected(string colorKey)
    {
        SendMessage(new RCDColorChangeMessage(_entityManager.GetNetEntity(Owner), colorKey));
    }

    private void OnRCDSystemMessage(ProtoId<RCDPrototype> protoId)
    {
        SendMessage(new RCDSystemMessage(protoId));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _menu != null)
        {
            _menu.ColorSelected -= OnColorSelected;
            _menu.SendRCDSystemMessageAction -= OnRCDSystemMessage;
        }

        base.Dispose(disposing);
    }
}
