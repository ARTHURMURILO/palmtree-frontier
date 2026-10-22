using Content.Shared.Whitelist;
using Robust.Shared.Analyzers;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.SmartFridge;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SmartFridgeSystem))]
public sealed partial class SmartFridgeComponent : Component
{
    /// <summary>
    /// The container ID that this SmartFridge stores its inventory in
    /// </summary>
    [DataField]
    public string Container = "smart_fridge_inventory";

    /// <summary>
    /// Whitelist for what entities can be inserted
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Blacklist for what entities can be inserted
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// The sound played on inserting an item into the fridge
    /// </summary>
    [DataField]
    public SoundSpecifier? InsertSound = new SoundCollectionSpecifier("MachineInsert");

    /// <summary>
    /// A list of entries to display in the UI
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<SmartFridgeEntry> Entries = new();

    /// <summary>
    /// A mapping of smart fridge entries to the actual contained contents
    /// </summary>
    /// <remarks>
    /// Palmtree: runtime UI state, deliberately not a <c>[DataField]</c>. <see cref="SmartFridgeEntry"/>
    /// is a mapping (it has no scalar form), so a non-empty dictionary cannot be serialized (a dictionary
    /// needs value-node keys) - a live grid save of a used fridge threw "Caught exception while
    /// serializing component SmartFridge" and failed the whole save (the bluespace dock's ship files).
    /// The values are also session-scoped <see cref="NetEntity"/>s. <see cref="SmartFridgeSystem"/>
    /// rebuilds this from the container on startup/MapInit instead.
    /// </remarks>
    [AutoNetworkedField]
    [Access(typeof(SmartFridgeSystem), Other = AccessPermissions.ReadExecute)]
    public Dictionary<SmartFridgeEntry, HashSet<NetEntity>> ContainedEntries = new();

    /// <summary>
    /// The flavour text displayed at the bottom of the SmartFridge's UI
    /// </summary>
    [DataField]
    public LocId FlavorText = "smart-fridge-request-generic";

    /// <summary>
    /// Sound that plays when ejecting an item
    /// </summary>
    [DataField]
    public SoundSpecifier SoundVend = new SoundCollectionSpecifier("VendingDispense")
    {
        Params = new AudioParams
        {
            Volume = -4f,
            Variation = 0.15f
        }
    };

    /// <summary>
    /// Sound that plays when an item can't be ejected
    /// </summary>
    [DataField]
    public SoundSpecifier SoundDeny = new SoundCollectionSpecifier("VendingDeny");

    // Frontier: extra fields
    /// <summary>
    /// The maximum number of entities that can be stored in the fridge
    /// </summary>
    [DataField]
    public int MaxContainedCount = 300;

    /// <summary>
    /// If true, insertion requires access
    /// </summary>
    [DataField]
    public bool CheckAccessOnInsert = true;
    // End Frontier
}

[Serializable, NetSerializable, DataRecord]
public record struct SmartFridgeEntry
{
    public string Name;

    public SmartFridgeEntry(string name)
    {
        Name = name;
    }
}

[Serializable, NetSerializable]
public enum SmartFridgeUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SmartFridgeDispenseItemMessage(SmartFridgeEntry entry) : BoundUserInterfaceMessage
{
    public SmartFridgeEntry Entry = entry;
}
