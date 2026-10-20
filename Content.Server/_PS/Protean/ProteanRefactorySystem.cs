using Content.Shared._PS.Modsuits;
using Content.Shared._PS.Protean;
using Content.Server._PS.Modsuits;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._PS.Protean;

/// <summary>
/// Refactory metabolism and the source Protean's missing-organ penalties.
/// </summary>
public sealed class ProteanRefactorySystem : EntitySystem
{
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";

    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ModsuitSystem _modsuits = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ProteanComponent, BodyComponent>();
        while (query.MoveNext(out var uid, out var protean, out var body))
        {
            if (protean.Folded)
                continue;

            // The refactory drives both the metal store and passive repair. Without it the swarm
            // degrades and slowly takes blunt damage.
            if (!_body.TryGetBodyOrganEntityComps<ProteanRefactoryComponent>((uid, body), out var factories))
            {
                SyncCore(protean.Suit, 0f, 10f);

                if (now >= protean.NextRefactoryDamage
                    && !_mobState.IsDead(uid))
                {
                    protean.NextRefactoryDamage = now + TimeSpan.FromSeconds(1);
                    var damage = new DamageSpecifier(
                        _prototypes.Index(BluntDamage),
                        3);
                    _damageable.TryChangeDamage(uid, damage, ignoreResistances: true,
                        interruptsDoAfters: false);
                }

                if (now >= protean.NextRefactoryWarning && !_mobState.IsDead(uid))
                {
                    protean.NextRefactoryWarning = now + TimeSpan.FromSeconds(30);
                    _popup.PopupEntity(Loc.GetString("protean-missing-refactory"), uid, uid);
                }
            }
            else
            {
                SyncCore(protean.Suit, factories[0].Comp1.Metal, factories[0].Comp1.MaxMetal);

                var lowPower = _statusEffects.HasStatusEffect(uid, "ProteanLowPowerStatusEffect");
                foreach (var refactory in factories)
                {
                    if (refactory.Comp1.Metal <= 0f)
                        continue;

                    var drain = (lowPower ? 0.0003f : 0.005f) * frameTime;
                    refactory.Comp1.Metal = Math.Max(0f, refactory.Comp1.Metal - drain);
                    Dirty(refactory.Owner, refactory.Comp1);
                }

                // High-metal Proteans slowly knit their swarm back together, mirroring the source's
                // passive brute/fire regeneration. It is intentionally small and stops at 30% metal.
                if (now >= protean.NextPassiveHeal
                    && factories.Count > 0
                    && factories[0].Comp1.Metal >= factories[0].Comp1.MaxMetal * 0.3f)
                {
                    protean.NextPassiveHeal = now + TimeSpan.FromSeconds(1);
                    var heal = new DamageSpecifier();
                    heal.DamageDict[BluntDamage] = -0.5;
                    heal.DamageDict[HeatDamage] = -0.5;
                    _damageable.TryChangeDamage(uid, heal, ignoreResistances: true,
                        interruptsDoAfters: false);
                }
            }

            // The orchestrator is the parallel-processing coordination organ. Without it movement
            // becomes difficult and the Protean periodically collapses.
            if (!_body.TryGetBodyOrganEntityComps<ProteanOrchestratorComponent>((uid, body), out _))
            {
                if (now >= protean.NextOrchestratorPenalty)
                {
                    protean.NextOrchestratorPenalty = now + TimeSpan.FromSeconds(5);
                    _statusEffects.TryAddStatusEffectDuration(uid,
                        "ProteanOrchestratorPenaltyStatusEffect",
                        TimeSpan.FromSeconds(6));
                    _stun.TryKnockdown((uid, null), TimeSpan.FromSeconds(1), force: true);
                }

                if (now >= protean.NextOrchestratorWarning)
                {
                    protean.NextOrchestratorWarning = now + TimeSpan.FromSeconds(30);
                    _popup.PopupEntity(Loc.GetString("protean-missing-orchestrator"), uid, uid);
                }
            }
            else
            {
                _statusEffects.TryRemoveStatusEffect(uid, "ProteanOrchestratorPenaltyStatusEffect");
            }
        }
    }

    private void SyncCore(EntityUid? suit, float metal, float maxMetal)
    {
        if (suit is not { } suitUid
            || !TryComp<ModsuitControlComponent>(suitUid, out var control)
            || control.Core is not { } coreUid
            || !TryComp<ModsuitCoreComponent>(coreUid, out var core)
            || !HasComp<ProteanCoreComponent>(coreUid))
            return;

        core.Charge = Math.Clamp(metal, 0f, Math.Max(maxMetal, 0.1f));
        core.MaxCharge = Math.Max(maxMetal, 0.1f);
        Dirty(coreUid, core);

        if (control.Active && core.Charge <= 0f)
            _modsuits.ForceRetract((suitUid, control));
    }
}
