using System.Numerics;
using Content.Client._PS.ShipCombat.UI;
using Robust.Client.Graphics;

// Purposefully colliding with the base namespace, like the _NF partial does.
namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private static readonly Color LockColor = Color.FromHex("#FF3B3B");
    private static readonly Color LockCursorColor = Color.FromHex("#FFE24D");
    private static readonly Color ArmedCursorYellow = Color.FromHex("#FFD700");
    private static readonly Color ArmedCursorOrange = Color.FromHex("#FF8C1A");
    private static readonly Color HostileTorpedoColor = Color.FromHex("#FF3B3B");
    private static readonly Color FriendlyTorpedoColor = Color.FromHex("#3BD63B");

    /// <summary>
    /// Palmtree: tactical console overlay data, set by the tactical console window. Null for every
    /// other radar user, in which case this partial draws nothing.
    /// </summary>
    public TacticalOverlayState? TacticalOverlay { get; set; }

    partial void DrawForkOverlay(DrawingHandleScreen handle, Matrix3x2 mapToView)
    {
        if (TacticalOverlay is not { } overlay)
            return;

        // The console's own lock target: a red square.
        if (overlay.LockTargetPosition is { } lockPosition)
        {
            var position = Vector2.Transform(lockPosition, mapToView);
            DrawBracketSquare(handle, position, LockColor);
        }

        // Ships locking us: yellow, flashing yellow/orange when they are armed.
        foreach (var cursor in overlay.LockCursors)
        {
            var position = Vector2.Transform(cursor.Position, mapToView);
            DrawLockCursor(handle, position, cursor.Armed);
        }

        foreach (var torpedo in overlay.Torpedoes)
        {
            var position = Vector2.Transform(torpedo.Position, mapToView);
            DrawDiamond(handle, position, torpedo.Friendly ? FriendlyTorpedoColor : HostileTorpedoColor);
        }
    }

    private void DrawLockCursor(DrawingHandleScreen handle, Vector2 position, bool armed)
    {
        if (!armed)
        {
            DrawBracketSquare(handle, position, LockCursorColor);
            return;
        }

        // Bright yellow/orange flashing.
        var flash = (int) (Timing.CurTime.TotalSeconds * 5) % 2 == 0;
        DrawBracketSquare(handle, position, flash ? ArmedCursorYellow : ArmedCursorOrange);
    }

    private static void DrawBracketSquare(DrawingHandleScreen handle, Vector2 position, Color color)
    {
        const float half = 14f;
        const float len = 8f;

        var topLeft = position + new Vector2(-half, -half);
        var topRight = position + new Vector2(half, -half);
        var bottomRight = position + new Vector2(half, half);
        var bottomLeft = position + new Vector2(-half, half);

        handle.DrawLine(topLeft, topLeft + new Vector2(len, 0f), color);
        handle.DrawLine(topLeft, topLeft + new Vector2(0f, len), color);
        handle.DrawLine(topRight, topRight + new Vector2(-len, 0f), color);
        handle.DrawLine(topRight, topRight + new Vector2(0f, len), color);
        handle.DrawLine(bottomRight, bottomRight + new Vector2(-len, 0f), color);
        handle.DrawLine(bottomRight, bottomRight + new Vector2(0f, -len), color);
        handle.DrawLine(bottomLeft, bottomLeft + new Vector2(len, 0f), color);
        handle.DrawLine(bottomLeft, bottomLeft + new Vector2(0f, -len), color);
    }

    private static void DrawDiamond(DrawingHandleScreen handle, Vector2 position, Color color)
    {
        const float size = 6f;
        var verts = new[]
        {
            position + new Vector2(0f, -size),
            position + new Vector2(size, 0f),
            position + new Vector2(0f, size),
            position + new Vector2(-size, 0f),
        };

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, verts, color.WithAlpha(0.85f));
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, verts, color);
    }
}
