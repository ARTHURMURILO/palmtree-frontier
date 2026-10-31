// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._NSV13.Reactor.UI;

/// <summary>
///     Minimal multi-series line chart, standing in for tgui's <c>Chart.Line</c>.
/// </summary>
/// <remarks>
///     The NSV13 reactor consoles draw their history with tgui's <c>Chart.Line</c> component
///     (RbmkStats.js, StormdriveConsole.js). Robust has no chart control, so the bedroom-sized
///     version of it lives here: a polyline per series over a shared grid, drawn straight into the
///     control's render handle. Series carry their own Y range, exactly like the <c>rangeY</c> prop,
///     because the reactor graphs mix 0-1500 PSI with -273-1227 °C.
/// </remarks>
public sealed class ReactorChart : Control
{
    /// <summary>One line on the chart.</summary>
    public sealed class ChartSeries
    {
        public float[] Values = Array.Empty<float>();
        public float Min;
        public float Max = 1f;
        public Color Colour = Color.White;
    }

    private readonly List<ChartSeries> _series = new();

    public Color BackgroundColour = new(0.08f, 0.09f, 0.10f);
    public Color GridColour = new(1f, 1f, 1f, 0.10f);
    public Color AxisColour = new(1f, 1f, 1f, 0.25f);

    /// <summary>Horizontal grid divisions.</summary>
    public int GridLines = 4;

    /// <summary>Vertical grid divisions.</summary>
    public int GridColumns = 8;

    public void SetSeries(IEnumerable<ChartSeries> series)
    {
        _series.Clear();
        _series.AddRange(series);
    }

    public void Clear()
    {
        _series.Clear();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;

        if (size.X < 2f || size.Y < 2f)
            return;

        handle.DrawRect(PixelSizeBox, BackgroundColour);

        for (var i = 1; i < GridLines; i++)
        {
            var y = size.Y * i / GridLines;
            handle.DrawLine(new Vector2(0f, y), new Vector2(size.X, y), GridColour);
        }

        for (var i = 1; i < GridColumns; i++)
        {
            var x = size.X * i / GridColumns;
            handle.DrawLine(new Vector2(x, 0f), new Vector2(x, size.Y), GridColour);
        }

        handle.DrawRect(new UIBox2(0f, size.Y - 1f, size.X, size.Y), AxisColour);

        foreach (var series in _series)
        {
            DrawSeries(handle, size, series);
        }
    }

    private static void DrawSeries(DrawingHandleScreen handle, Vector2 size, ChartSeries series)
    {
        var values = series.Values;

        if (values.Length < 2)
            return;

        var range = MathF.Max(series.Max - series.Min, 0.0001f);
        var stepX = size.X / (values.Length - 1);

        var previous = ToScreen(values[0], 0f);

        for (var i = 1; i < values.Length; i++)
        {
            var current = ToScreen(values[i], i * stepX);
            handle.DrawLine(previous, current, series.Colour);
            previous = current;
        }

        Vector2 ToScreen(float value, float x)
        {
            var normalised = Math.Clamp((value - series.Min) / range, 0f, 1f);
            return new Vector2(x, size.Y - normalised * size.Y);
        }
    }
}
