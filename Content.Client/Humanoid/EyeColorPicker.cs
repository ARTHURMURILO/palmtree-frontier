using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._PS.UserInterface; // Palmtree: color clipboard

namespace Content.Client.Humanoid;

public sealed class EyeColorPicker : Control
{
    public event Action<Color>? OnEyeColorPicked;

    private readonly ColorSelectorWithClipboard _colorSelectors;

    private Color _lastColor;

    public void SetData(Color color)
    {
        _lastColor = color;

        _colorSelectors.Color = color;
    }

    public EyeColorPicker()
    {
        var vBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical
        };
        AddChild(vBox);

        vBox.AddChild(_colorSelectors = new ColorSelectorWithClipboard());
        _colorSelectors.SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv; // defaults color selector to HSV

        _colorSelectors.OnColorChanged += ColorValueChanged;
    }

    private void ColorValueChanged(Color newColor)
    {
        OnEyeColorPicked?.Invoke(newColor);

        _lastColor = newColor;
    }
}
