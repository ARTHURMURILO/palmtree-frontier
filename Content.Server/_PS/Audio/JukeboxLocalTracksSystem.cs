using Content.Server.Storage.EntitySystems;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Audio;

/// <summary>
/// Injects the Palmtree jukebox record into jukeboxes when the local track prototypes exist.
/// The record, its jukebox definitions and the audio live in gitignored folders because they
/// contain copyrighted music, so on a clean checkout the prototype simply is not indexed and
/// this system does nothing.
/// </summary>
public sealed class JukeboxLocalTracksSystem : EntitySystem
{
    private const string RecordPrototype = "PalmtreeRecord";

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly StorageSystem _storage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JukeboxComponent, MapInitEvent>(OnJukeboxMapInit);
    }

    private void OnJukeboxMapInit(Entity<JukeboxComponent> ent, ref MapInitEvent args)
    {
        if (!_prototypes.HasIndex<EntityPrototype>(RecordPrototype))
            return;

        if (!TryComp<StorageComponent>(ent, out var storage))
            return;

        // Maps saved with the record already inside should not get a second one.
        foreach (var contained in storage.Container.ContainedEntities)
        {
            if (MetaData(contained).EntityPrototype?.ID == RecordPrototype)
                return;
        }

        var record = Spawn(RecordPrototype, Transform(ent).Coordinates);
        if (!_storage.Insert(ent, record, out _, playSound: false))
            QueueDel(record);
    }
}
