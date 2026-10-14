using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Security;

/// <summary>
/// Palmtree: marks a grid (the NFSD vault) as off-limits. Characters standing on it that do not
/// hold one of the <see cref="ExemptDepartments"/> jobs are flagged as wanted.
/// </summary>
[RegisterComponent]
public sealed partial class NfsdVaultTrespassComponent : Component
{
    /// <summary>Departments whose jobs are allowed on the grid (NFSD is Security, plus Command).</summary>
    [DataField]
    public List<ProtoId<DepartmentPrototype>> ExemptDepartments = new() { "Security", "Command" };

    /// <summary>Reason written onto the trespasser's criminal record.</summary>
    [DataField]
    public LocId Reason = "nfsd-vault-trespass-reason";
}
