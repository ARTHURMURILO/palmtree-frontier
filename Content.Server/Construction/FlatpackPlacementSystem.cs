using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Construction;

/// <summary>
///     Validates the footprint of multi-tile flatpacks before they unpack.
/// </summary>
public sealed class FlatpackPlacementSystem : EntitySystem
{
    [Dependency] private readonly SharedFlatpackPlacementSystem _placement = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FlatpackFootprintComponent, FlatpackUnpackAttemptEvent>(OnUnpackAttempt);
        SubscribeLocalEvent<FlatpackUnpackedEvent>(OnUnpacked);
    }

    private void OnUnpackAttempt(Entity<FlatpackFootprintComponent> ent, ref FlatpackUnpackAttemptEvent args)
    {
        if (!TryComp<MapGridComponent>(args.Grid, out var grid))
            return;

        if (_placement.CanPlace(ent, args.Grid, grid, args.Position, out var reason))
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString(reason ?? "flatpack-unpack-blocked");
    }

    private void OnUnpacked(FlatpackUnpackedEvent args)
    {
        if (!HasComp<FlatpackFootprintComponent>(args.Flatpack) || TerminatingOrDeleted(args.Spawned))
            return;

        // Multi-tile machines are placed as a unit, so they arrive anchored. A wrench still lets
        // the crew move one after unpacking.
        var xform = Transform(args.Spawned);
        if (!xform.Anchored)
            _transform.AnchorEntity(args.Spawned);
    }
}
