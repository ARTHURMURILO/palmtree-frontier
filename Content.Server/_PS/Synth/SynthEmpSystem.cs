using Content.Server.Emp;
using Content.Shared._PS.Synth;
using Content.Shared.Damage;

namespace Content.Server._PS.Synth;

/// <summary>
/// Palmtree: EMP and ion pulses are deadly to synths - their chassis is electronics.
/// </summary>
public sealed class SynthEmpSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SynthComponent, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnEmpPulse(Entity<SynthComponent> ent, ref EmpPulseEvent args)
    {
        // Damage modifiers apply, so the synth's shock weakness amplifies this further.
        _damageable.TryChangeDamage(ent.Owner, ent.Comp.EmpDamage);

        // Mark as affected so the EMP spark effect is spawned on the synth.
        args.Affected = true;
    }
}
