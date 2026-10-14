using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.BluespaceDock;

/// <summary>
/// One ship stored in the bluespace dock. The registry is the authoritative record: the ship file
/// is only the payload used to spawn the ship again.
/// </summary>
[DataDefinition]
public sealed partial class BluespaceDockEntry
{
    /// <summary>Stable id used by the UI and as the ship file name.</summary>
    [DataField]
    public string ShipId = string.Empty;

    /// <summary>Ship file path, relative to <see cref="BluespaceDockSystem.ShipDirectory"/>.</summary>
    [DataField]
    public string File = string.Empty;

    /// <summary>NetUserId (ckey) of the character that stored the ship.</summary>
    [DataField]
    public string OwnerUserId = string.Empty;

    /// <summary>Character name (ID card name) that stored the ship.</summary>
    [DataField]
    public string OwnerName = string.Empty;

    /// <summary>Vessel prototype id, when the ship came from a shipyard.</summary>
    [DataField]
    public string? Vessel;

    /// <summary>Model shown in the dock list.</summary>
    [DataField]
    public string Model = "Unknown";

    /// <summary>Base name of the ship (deed name without the registration suffix).</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>Full deed name at storage time (name + registration suffix).</summary>
    [DataField]
    public string DisplayName = "Unknown";

    /// <summary>Registration suffix at storage time.</summary>
    [DataField]
    public string? Suffix;

    /// <summary>Appraised value at storage time (retrieve tax and stored sale use this).</summary>
    [DataField]
    public int Value;

    /// <summary>ISO 8601 UTC timestamp of when the ship was stored.</summary>
    [DataField]
    public string StoredAt = string.Empty;
}

/// <summary>
/// The dock registry file (<c>UserData/bluespace_dock/registry.yml</c>).
/// </summary>
[DataDefinition]
public sealed partial class BluespaceDockRegistry
{
    [DataField]
    public List<BluespaceDockEntry> Entries = new();

    /// <summary>
    /// The entry that points at a given ship file, if any.
    /// </summary>
    public BluespaceDockEntry? FindByFile(string file)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.File, file, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public BluespaceDockEntry? Find(string shipId)
    {
        foreach (var entry in Entries)
        {
            if (entry.ShipId == shipId)
                return entry;
        }

        return null;
    }
}

/// <summary>
/// Vessel identity used for the dock's civilian check and its Model column.
/// </summary>
public readonly record struct BluespaceDockVesselInfo(
    ProtoId<VesselPrototype>? Vessel,
    string Group,
    string Model);
