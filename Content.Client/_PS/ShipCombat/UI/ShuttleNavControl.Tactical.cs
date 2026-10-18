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
        const float radius = 12f;
        const float tick = 5f;

        handle.DrawCircle(position, radius, LockColor, false);
        handle.DrawLine(position + new Vector2(-radius - tick, 0f), position + new Vector2(-radius, 0f), LockColor);
        handle.DrawLine(position + new Vector2(radius, 0f), position + new Vector2(radius + tick, 0f), LockColor);
        handle.DrawLine(position + new Vector2(0f, -radius - tick), position + new Vector2(0f, -radius), LockColor);
        handle.DrawLine(position + new Vector2(0f, radius), position + new Vector2(0f, radius + tick), LockColor);
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
