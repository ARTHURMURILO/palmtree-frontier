using Content.Shared._PS.Security;
using Content.Shared.Humanoid;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._PS.Security;

/// <summary>
/// Palmtree: sweeps characters standing on a grid carrying <see cref="NfsdVaultTrespassComponent"/>
/// (the NFSD vault) and flags anyone that is not NFSD or Command as wanted. Characters without a
/// record (no ID card and no sector record) are ignored.
/// </summary>
public sealed class NfsdVaultTrespassSystem : EntitySystem
{
    /// <summary>How often the characters are checked; "stepping on" should feel immediate.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5f);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly WantedStatusSystem _wanted = default!;

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        // Walking the characters instead of the grid's children catches mobs that are buckled to
        // furniture or inside containers on the vault as well.
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid ||
                !TryComp<NfsdVaultTrespassComponent>(grid, out var trespass))
            {
                continue;
            }

            if (_wanted.IsWanted(uid))
                continue;

            if (_wanted.TryGetJob(uid, out var job) && IsExempt(job, trespass))
                continue;

            _wanted.TrySetWanted(uid, Loc.GetString(trespass.Reason));
        }
    }

    /// <summary>True when the character's job belongs to one of the exempt departments.</summary>
    private bool IsExempt(ProtoId<JobPrototype> job, NfsdVaultTrespassComponent comp)
    {
        foreach (var departmentId in comp.ExemptDepartments)
        {
            if (!_prototype.TryIndex(departmentId, out var department))
                continue;

            if (department.Roles.Contains(job))
                return true;
        }

        return false;
    }
}
