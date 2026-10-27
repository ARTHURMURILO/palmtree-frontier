using Content.Server.Spawners.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Audio;

/// <summary>
/// Handles the Palmtree jukebox records that live in gitignored folders.
/// The Palm Record is injected into every jukebox, while the Starjunk record is added to the
/// dungeon record spawner at runtime so clean checkouts stay free of copyrighted audio paths.
/// </summary>
public sealed class JukeboxLocalTracksSystem : EntitySystem
{
    private const string DefaultRecordPrototype = "PalmtreeRecord";
    private const string DungeonRecordPrototype = "StarjunkRecord";
    private const string DungeonRecordSpawner = "NFJukeboxRecordSpawner";

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly StorageSystem _storage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JukeboxComponent, MapInitEvent>(OnJukeboxMapInit);
        // Palmtree: use ComponentInit, not MapInit. ConditionalSpawnerSystem already owns the
        // RandomSpawnerComponent/MapInitEvent subscription and this event bus allows only one
        // directed subscription per component/event pair (a second one crashes server startup).
        // ComponentInit runs before map init, so the loot pool is ready before the spawner rolls.
        SubscribeLocalEvent<RandomSpawnerComponent, ComponentInit>(OnRecordSpawnerInit);
    }

    private void OnJukeboxMapInit(Entity<JukeboxComponent> ent, ref MapInitEvent args)
    {
        if (!_prototypes.HasIndex<EntityPrototype>(DefaultRecordPrototype))
            return;

        if (!TryComp<StorageComponent>(ent, out var storage))
            return;

        if (ContainsRecord(storage, DefaultRecordPrototype))
            return;

        var record = Spawn(DefaultRecordPrototype, Transform(ent).Coordinates);
        if (!_storage.Insert(ent, record, out _, playSound: false))
            QueueDel(record);
    }

    private void OnRecordSpawnerInit(Entity<RandomSpawnerComponent> ent, ref ComponentInit args)
    {
        if (MetaData(ent).EntityPrototype?.ID != DungeonRecordSpawner)
            return;

        if (!_prototypes.HasIndex<EntityPrototype>(DungeonRecordPrototype))
            return;

        foreach (var prototype in ent.Comp.Prototypes)
        {
            if (prototype.Id == DungeonRecordPrototype)
                return;
        }

        ent.Comp.Prototypes.Add(DungeonRecordPrototype);
    }

    private bool ContainsRecord(StorageComponent storage, string recordPrototype)
    {
        foreach (var contained in storage.Container.ContainedEntities)
        {
            if (MetaData(contained).EntityPrototype?.ID == recordPrototype)
                return true;
        }

        return false;
    }
}
