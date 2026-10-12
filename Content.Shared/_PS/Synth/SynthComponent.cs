using Content.Shared.Damage;

namespace Content.Shared._PS.Synth;

/// <summary>
/// Marker component for Synth entities. Synths are synthetic and do not need to breathe;
/// systems that do not apply to them (e.g. respiration) check for this component.
/// </summary>
[RegisterComponent]
public sealed partial class SynthComponent : Component
{
    /// <summary>
    /// Palmtree: damage taken from an EMP or ion pulse. A synth's chassis is electronics, so an
    /// EMP is deadly to them. Damage modifiers still apply, so the shock weakness amplifies this.
    /// </summary>
    [DataField]
    public DamageSpecifier EmpDamage = new()
    {
        DamageDict = new()
        {
            { "Shock", 100 },
        },
    };
}
