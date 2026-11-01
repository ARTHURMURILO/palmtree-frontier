// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Resources;
using Content.Shared._NSV13.Reactor;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;

namespace Content.Client._NSV13.Reactor;

/// <summary>
///     Paints the reactor's connection faces around it while the local player is examining it.
/// </summary>
/// <remarks>
///     Purely cosmetic and purely local: nothing here is networked, so only the player who opened
///     the examine window sees the arrows. The sprite is left untouched - the whole point is to
///     answer "which way is up" without editing the art.
///
///     Drawn in screen space rather than world space for one reason: the labels have to stay
///     upright and readable while the machine, and therefore the arrows, are rotated with the hull.
///     World-space text does not exist in Robust, and mixing a world-space arrow overlay with a
///     screen-space label overlay is two overlays for one job.
/// </remarks>
public sealed class ReactorPortOverlay : Overlay
{
    private const string FontPath = "/Fonts/NotoSans/NotoSans-Regular.ttf";
    private const int FontSize = 12;

    /// <summary>Where the arrow leaves the machine, in tiles from its centre.</summary>
    private const float ArrowStart = 1.65f;

    /// <summary>Where the arrow head ends, in tiles from the machine's centre.</summary>
    private const float ArrowEnd = 2.45f;

    /// <summary>Clearance between the arrow head and the label, in pixels.</summary>
    private const float LabelGap = 4f;

    private const float HeadLength = 9f;
    private const float HeadWidth = 5f;

    private static readonly Color CoolantInColor = Color.FromHex("#59C2FF");
    private static readonly Color CoolantOutColor = Color.FromHex("#FF7A3C");
    private static readonly Color ModeratorColor = Color.FromHex("#C77DFF");

    private readonly IEntityManager _entMan;
    private readonly SharedTransformSystem _xform;
    private readonly Font _font;

    /// <summary>The machine being annotated, or null when nothing is being inspected.</summary>
    public EntityUid? Target;

    /// <summary>Fade multiplier, so the overlay can dim out instead of popping.</summary>
    public float Alpha = 1f;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public ReactorPortOverlay(IEntityManager entMan, IResourceCache cache, SharedTransformSystem xform)
    {
        _entMan = entMan;
        _xform = xform;
        _font = cache.GetFont(FontPath, FontSize);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (Target is not { } uid || Alpha <= 0.01f)
            return;

        if (args.ViewportControl is not { } viewport)
            return;

        if (!_entMan.TryGetComponent(uid, out ReactorPortLayoutComponent? layout))
            return;

        var centre = _xform.GetWorldPosition(uid);
        var rotation = _xform.GetWorldRotation(uid);
        var handle = args.ScreenHandle;

        DrawPort(handle, viewport, centre, rotation, layout.CoolantIn, "reactor-port-coolant-in", CoolantInColor);
        DrawPort(handle, viewport, centre, rotation, layout.CoolantOut, "reactor-port-coolant-out", CoolantOutColor);
        DrawPort(handle, viewport, centre, rotation, layout.Moderator, "reactor-port-moderator", ModeratorColor);
    }

    private void DrawPort(
        DrawingHandleScreen handle,
        IViewportControl viewport,
        Vector2 centre,
        Angle rotation,
        Direction face,
        LocId label,
        Color color)
    {
        if (face == Direction.Invalid)
            return;

        var unitWorld = rotation.RotateVec(face.ToVec());
        var tail = viewport.WorldToScreen(centre + unitWorld * ArrowStart);
        var tip = viewport.WorldToScreen(centre + unitWorld * ArrowEnd);

        var delta = tip - tail;
        var length = delta.Length();

        if (length < 2f)
            return;

        var along = delta / length;
        var normal = new Vector2(-along.Y, along.X);
        var shade = color.WithAlpha(0.9f * Alpha);

        // Shaft. Three one-pixel lines so it reads as a solid stroke at the default UI scale.
        var headStart = tip - along * MathF.Min(HeadLength, length * 0.5f);
        for (var i = -1; i <= 1; i++)
        {
            handle.DrawLine(tail + normal * i, headStart + normal * i, shade);
        }

        // Head, doubled so the wings carry the same weight as the shaft.
        for (var i = -0.5f; i <= 0.5f; i += 1f)
        {
            var basePoint = headStart + normal * i;
            handle.DrawLine(tip, basePoint + normal * HeadWidth, shade);
            handle.DrawLine(tip, basePoint - normal * HeadWidth, shade);
        }

        // Label, drawn upright and shadowed so it survives a bright reactor floor.
        var text = Loc.GetString(label);
        var size = handle.GetDimensions(_font, text, 1f);

        // Caption the arrow without running into it. A label centred past the head of a sideways
        // arrow is wide enough to reach back over the shaft ("MODERATOR" is the worst offender), so
        // those get captioned from above instead. Vertical arrows have the room ahead of them.
        Vector2 labelCentre;
        if (MathF.Abs(along.X) > MathF.Abs(along.Y))
        {
            labelCentre = tip + new Vector2(0f, -(size.Y * 0.5f + LabelGap));
        }
        else
        {
            // Half the label's extent measured along the arrow, not just half its height: a label
            // pushed out by half its height still clips a head that is coming in at an angle.
            var halfAlong = MathF.Abs(along.X) * size.X * 0.5f + MathF.Abs(along.Y) * size.Y * 0.5f;
            labelCentre = tip + along * (halfAlong + LabelGap);
        }

        var origin = labelCentre - size / 2f;

        handle.DrawString(_font, origin + new Vector2(1f, 1f), text, Color.Black.WithAlpha(0.8f * Alpha));
        handle.DrawString(_font, origin, text, color.WithAlpha(Alpha));
    }
}
