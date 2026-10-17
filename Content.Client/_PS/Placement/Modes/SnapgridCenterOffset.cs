using System.Numerics;
using Content.Shared._PS.CCVar;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.Client._PS.Placement.Modes;

/// <summary>
/// Palmtree: snaps to the grid tile center (plus the prototype's integer placement offset) and then
/// applies the optional fractional offset from <see cref="PSCVars.PlacementOffsetX"/> and
/// <see cref="PSCVars.PlacementOffsetY"/>, in tiles.
/// </summary>
/// <remarks>
/// Content-side placement modes are picked up automatically by <c>PlacementManager</c> and listed
/// in every placement mode dropdown (the spawn menu's Override menu and the map editor's entity
/// placement selector). With a zero offset this behaves exactly like the engine's SnapgridCenter.
/// </remarks>
public sealed class SnapgridCenterOffset : SnapgridCenter
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public SnapgridCenterOffset(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        base.AlignPlacementMode(mouseScreen);

        if (pManager.CurrentPermission is { IsTile: true })
            return;

        var offset = new Vector2(
            _cfg.GetCVar(PSCVars.PlacementOffsetX),
            _cfg.GetCVar(PSCVars.PlacementOffsetY));

        if (offset == Vector2.Zero)
            return;

        MouseCoords = MouseCoords.Offset(offset);
    }
}
