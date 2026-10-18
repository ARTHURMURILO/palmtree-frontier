using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.LayoutContainer;

namespace Content.Client._PS.ShipCombat.UI;

/// <summary>
/// Palmtree: a power pool slider with a marker line at its threshold (the point from which the pool
/// overpowers its system).
/// </summary>
public sealed class ThresholdSlider : Slider
{
    private readonly PanelContainer _thresholdMarker;

    /// <summary>Value the marker line sits at.</summary>
    public float Threshold { get; set; } = 100f;

    public ThresholdSlider()
    {
        _thresholdMarker = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#FFD700") },
            MinSize = new Vector2(2f, 0f),
            MouseFilter = MouseFilterMode.Ignore,
        };

        AddChild(_thresholdMarker);
        SetAnchorBottom(_thresholdMarker, 1);
        SetGrowHorizontal(_thresholdMarker, GrowDirection.Both);
        SetGrowVertical(_thresholdMarker, GrowDirection.Both);
    }

    protected override void Resized()
    {
        base.Resized();
        UpdateThreshold();
    }

    private void UpdateThreshold()
    {
        var range = MaxValue - MinValue;
        if (range <= 0f)
            return;

        var x = Width * (Threshold - MinValue) / range;
        SetMarginLeft(_thresholdMarker, x);
        SetMarginRight(_thresholdMarker, Width - x);
    }
}
