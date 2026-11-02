using Content.Shared._Goobstation.BlankDisk;
using Content.Shared._Goobstation.StationRadio.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Goobstation.BlankDisk;

/// <summary>
/// Turns a blank disk into a copy of any vinyl disk prototype the player picks.
/// </summary>
public sealed class BlankVinylDiskSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlankVinylDiskComponent, BlankVinylDiskSelectMessage>(OnSelect);
    }

    private void OnSelect(Entity<BlankVinylDiskComponent> ent, ref BlankVinylDiskSelectMessage args)
    {
        if (!_proto.TryIndex(args.Prototype, out EntityPrototype? prototype))
            return;

        // Only real, imprintable records are allowed.
        if (!prototype.TryGetComponent<VinylComponent>("Vinyl", out var vinyl) || vinyl.Song == null)
            return;

        var xform = Transform(ent.Owner);
        _containers.TryGetContainingContainer((ent.Owner, xform, MetaData(ent.Owner)), out var container);

        var replacement = Spawn(args.Prototype, xform.Coordinates);
        if (container != null && !_containers.Insert((replacement, null, null, null), container))
            Log.Warning($"Failed to insert imprinted disk into {ToPrettyString(container.Owner)}");

        QueueDel(ent.Owner);
    }
}
