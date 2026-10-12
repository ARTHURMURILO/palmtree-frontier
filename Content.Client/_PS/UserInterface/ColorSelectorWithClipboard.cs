using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._PS.UserInterface;

/// <summary>
/// Palmtree: a <see cref="ColorSelectorSliders"/> with [Copy] and [Paste] buttons underneath,
/// so a color can be copied from one picker and pasted into another.
/// </summary>
public sealed class ColorSelectorWithClipboard : BoxContainer
{
    /// <summary>
    /// The most recently copied color, shared by every color picker on the client.
    /// </summary>
    public static Color? Clipboard;

    private static event Action? ClipboardChanged;

    private readonly ColorSelectorSliders _selector;
    private readonly Button _pasteButton;

    public ColorSelectorWithClipboard()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 2;

        _selector = new ColorSelectorSliders
        {
            HorizontalExpand = true,
        };
        _selector.OnColorChanged += color => OnColorChanged?.Invoke(color);
        AddChild(_selector);

        var buttons = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
        };
        AddChild(buttons);

        var copyButton = new Button
        {
            Text = Loc.GetString("color-clipboard-copy"),
            ToolTip = Loc.GetString("color-clipboard-copy-tooltip"),
            HorizontalExpand = true,
        };
        copyButton.OnPressed += _ =>
        {
            Clipboard = _selector.Color;
            ClipboardChanged?.Invoke();
        };
        buttons.AddChild(copyButton);

        _pasteButton = new Button
        {
            Text = Loc.GetString("color-clipboard-paste"),
            ToolTip = Loc.GetString("color-clipboard-paste-tooltip"),
            HorizontalExpand = true,
        };
        _pasteButton.OnPressed += _ =>
        {
            if (Clipboard is not { } color)
                return;

            // The color selector's Color setter is silent, so tell listeners about the paste ourselves.
            _selector.Color = color;
            OnColorChanged?.Invoke(color);
        };
        buttons.AddChild(_pasteButton);

        ClipboardChanged += UpdatePasteButton;
        UpdatePasteButton();
    }

    /// <summary>
    /// The currently selected color. Setting this does not fire <see cref="OnColorChanged"/>.
    /// </summary>
    public Color Color
    {
        get => _selector.Color;
        set => _selector.Color = value;
    }

    /// <summary>
    /// Which color space the sliders use.
    /// </summary>
    public ColorSelectorSliders.ColorSelectorType SelectorType
    {
        get => _selector.SelectorType;
        set => _selector.SelectorType = value;
    }

    /// <summary>
    /// Whether the alpha slider is shown.
    /// </summary>
    public bool IsAlphaVisible
    {
        get => _selector.IsAlphaVisible;
        set => _selector.IsAlphaVisible = value;
    }

    /// <summary>
    /// Fired when the sliders move, or when a color is pasted.
    /// </summary>
    public event Action<Color>? OnColorChanged;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            ClipboardChanged -= UpdatePasteButton;
    }

    private void UpdatePasteButton()
    {
        _pasteButton.Disabled = Clipboard == null;
    }
}
