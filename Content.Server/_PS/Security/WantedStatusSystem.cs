using Content.Server._NF.SectorServices;
using Content.Server.CriminalRecords.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.CriminalRecords;
using Content.Shared.IdentityManagement;
using Content.Shared.Roles;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Security;

/// <summary>
/// Palmtree: small helper around the (sector-wide) criminal records, so features can read and set a
/// character's wanted status without walking the ID card / record key plumbing themselves.
/// </summary>
public sealed class WantedStatusSystem : EntitySystem
{
    [Dependency] private readonly CriminalRecordsSystem _criminalRecords = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly SectorServiceSystem _sectorService = default!;

    /// <summary>Status of the character's criminal record, if one can be found.</summary>
    public bool TryGetStatus(EntityUid mob, out SecurityStatus status)
    {
        status = SecurityStatus.None;

        if (!TryGetRecordKey(mob, out var key) ||
            !_records.TryGetRecord<CriminalRecord>(key, out var record))
        {
            return false;
        }

        status = record.Status;
        return true;
    }

    /// <summary>Job recorded on the character's general record, if one can be found.</summary>
    public bool TryGetJob(EntityUid mob, out ProtoId<JobPrototype> job)
    {
        job = default;

        if (!TryGetRecordKey(mob, out var key) ||
            !_records.TryGetRecord<GeneralStationRecord>(key, out var record) ||
            string.IsNullOrEmpty(record.JobPrototype))
        {
            return false;
        }

        job = new ProtoId<JobPrototype>(record.JobPrototype);
        return true;
    }

    /// <summary>True when the character's criminal record is flagged as wanted.</summary>
    public bool IsWanted(EntityUid mob)
    {
        return TryGetStatus(mob, out var status) && status == SecurityStatus.Wanted;
    }

    /// <summary>
    /// Flags the character as wanted on their criminal record and mirrors the wanted icon onto them.
    /// Returns false when no record can be found (e.g. entities without an ID card, like simple mobs).
    /// </summary>
    public bool TrySetWanted(EntityUid mob, string reason)
    {
        if (!TryGetRecordKey(mob, out var key) ||
            !_records.TryGetRecord<CriminalRecord>(key, out var record))
        {
            return false;
        }

        _criminalRecords.OverwriteStatus(key, record, SecurityStatus.Wanted, reason);
        return true;
    }

    /// <summary>
    /// Finds the character's record key on the sector-wide records (the same record the criminal
    /// records console and the wanted list read).
    /// </summary>
    public bool TryGetRecordKey(EntityUid mob, out StationRecordKey key)
    {
        key = default;

        var station = _sectorService.GetServiceEntity();
        if (!station.IsValid())
            return false;

        // Preferred: the key stored on the character's own ID card, when it points at the sector records.
        if (_idCard.TryFindIdCard(mob, out var idCard) &&
            TryComp<StationRecordKeyStorageComponent>(idCard, out var keyStorage) &&
            keyStorage.Key is { } stored &&
            stored.OriginStation == station)
        {
            key = stored;
            return true;
        }

        // Fallback: match the character's identity name against the sector records (identity name, so
        // renamed mobs still resolve).
        var name = Identity.Name(mob, EntityManager);
        if (_records.GetRecordByName(station, name) is not { } id)
            return false;

        key = new StationRecordKey(id, station);
        return true;
    }
}
