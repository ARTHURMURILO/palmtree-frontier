using System.Numerics;
using Content.Client._PS.ShipCombat.UI;
using Robust.Client.Graphics;

// Purposefully colliding with the base namespace, like the _NF partial does.
namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private static readonly Color LockColor = Color.FromHex("#FF3B3B");
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

        if (overlay.LockTargetPosition is { } lockPosition)
        {
            var position = Vector2.Transform(lockPosition, mapToView);
            DrawLockReticle(handle, position);
        }

        foreach (var torpedo in overlay.Torpedoes)
        {
            var position = Vector2.Transform(torpedo.Position, mapToView);
            DrawDiamond(handle, position, torpedo.Friendly ? FriendlyTorpedoColor : HostileTorpedoColor);
        }
    }

    private static void DrawLockReticle(DrawingHandleScreen handle, Vector2 position)
    {
        // A red square of corner brackets around the locked contact.
        const float half = 14f;
        const float len = 8f;

        var topLeft = position + new Vector2(-half, -half);
        var topRight = position + new Vector2(half, -half);
        var bottomRight = position + new Vector2(half, half);
        var bottomLeft = position + new Vector2(-half, half);

        handle.DrawLine(topLeft, topLeft + new Vector2(len, 0f), LockColor);
        handle.DrawLine(topLeft, topLeft + new Vector2(0f, len), LockColor);
        handle.DrawLine(topRight, topRight + new Vector2(-len, 0f), LockColor);
        handle.DrawLine(topRight, topRight + new Vector2(0f, len), LockColor);
        handle.DrawLine(bottomRight, bottomRight + new Vector2(-len, 0f), LockColor);
        handle.DrawLine(bottomRight, bottomRight + new Vector2(0f, -len), LockColor);
        handle.DrawLine(bottomLeft, bottomLeft + new Vector2(len, 0f), LockColor);
        handle.DrawLine(bottomLeft, bottomLeft + new Vector2(0f, -len), LockColor);
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
