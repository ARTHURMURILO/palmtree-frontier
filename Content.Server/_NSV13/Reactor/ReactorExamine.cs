// SPDX-License-Identifier: AGPL-3.0-or-later


namespace Content.Server._NSV13.Reactor;

/// <summary>
///     Shared examine-text helpers for the two ported engines.
/// </summary>
public static class ReactorExamine
{
    /// <summary>
    ///     Turn a local face of a machine into the side the crew sees it on.
    /// </summary>
    /// <remarks>
    ///     The engine art turns with the hull and the camera counter-rotates with it, so the grid
    ///     frame is the screen frame: the machine's local west face is on the player's left no
    ///     matter which way the ship is pointed. Using the local rotation (rather than the world
    ///     one) is what makes that true for a machine that has itself been rotated.
    /// </remarks>
    public static string ScreenSide(Angle rotation, Direction local)
    {
        var side = rotation.RotateVec(local.ToVec());

        if (MathF.Abs(side.X) >= MathF.Abs(side.Y))
            return Loc.GetString(side.X >= 0f ? "reactor-side-right" : "reactor-side-left");

        return Loc.GetString(side.Y >= 0f ? "reactor-side-up" : "reactor-side-down");
    }
}
