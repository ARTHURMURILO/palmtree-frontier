using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
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

        SubscribeLocalEvent<JukeboxComponent, MapInitEvent>(OnJukeboxMapInit,
            before: new[] { typeof(ConditionalSpawnerSystem) });
        // Palmtree: RandomSpawnerComponent/MapInitEvent is already taken by ConditionalSpawnerSystem
        // (the directed event bus only allows one subscription per component/event pair). Subscribe
        // through MetaDataComponent instead and run before the spawner's own MapInit handler, so the
        // loot pool is extended on real map init without touching prototype data on uninitialized
        // maps (which would fail PrototypeSaveTest).
        SubscribeLocalEvent<MetaDataComponent, MapInitEvent>(OnRecordSpawnerInit,
            before: new[] { typeof(ConditionalSpawnerSystem) });
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

    private void OnRecordSpawnerInit(Entity<MetaDataComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.EntityPrototype?.ID != DungeonRecordSpawner)
            return;

        if (!_prototypes.HasIndex<EntityPrototype>(DungeonRecordPrototype))
            return;

        if (!TryComp<RandomSpawnerComponent>(ent.Owner, out var spawner))
            return;

        foreach (var prototype in spawner.Prototypes)
        {
            if (prototype.Id == DungeonRecordPrototype)
                return;
        }

        spawner.Prototypes.Add(DungeonRecordPrototype);
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
