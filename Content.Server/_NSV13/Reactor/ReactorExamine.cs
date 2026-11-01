// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._NSV13.Reactor;

/// <summary>
///     Shared examine-text helpers for the ported reactors.
/// </summary>
public static class ReactorExamine
{
    /// <summary>
    ///     Turn a machine face into the side the examiner sees it on.
    /// </summary>
    /// <param name="machineRotation">World rotation of the machine.</param>
    /// <param name="viewerFrame">World rotation of the grid the examiner is standing on.</param>
    /// <param name="local">The face in the machine's own frame.</param>
    /// <remarks>
    ///     The camera counter-rotates with the grid the <em>examiner</em> stands on, so the screen
    ///     frame is that grid's frame - not the machine's. On the same grid the two agree and this
    ///     reduces to the machine's local rotation; when the machine sits on a different, rotated
    ///     hull (a docked shuttle, say) the local rotation alone is wrong, which is what made a
    ///     rotated reactor report "west" for a port the player saw on their right.
    /// </remarks>
    public static string ScreenSide(Angle machineRotation, Angle viewerFrame, Direction local)
    {
        var side = new Angle(machineRotation.Theta - viewerFrame.Theta).RotateVec(local.ToVec());

        if (MathF.Abs(side.X) >= MathF.Abs(side.Y))
            return Loc.GetString(side.X >= 0f ? "reactor-side-right" : "reactor-side-left");

        return Loc.GetString(side.Y >= 0f ? "reactor-side-up" : "reactor-side-down");
    }
}
