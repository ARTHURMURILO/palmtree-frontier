using System.Linq;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._PS.Weapons.Melee;

/// <summary>
/// Handles the combat-mode rules for whips: lashes outside combat mode are harmless,
/// while combat-mode lashes deal the weapon's regular damage and stamina damage.
/// </summary>
public sealed class WhipSystem : EntitySystem
{
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WhipComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<WhipComponent> ent, ref MeleeHitEvent args)
    {
        if (!_combat.IsInCombatMode(args.User))
        {
            // A harmless lash: cancel the damage and hit reactions, but still apply a little stamina.
            args.Handled = true;
            ApplyStamina(ent, args, ent.Comp.OutOfCombatStaminaDamage);

            if (args.IsHit &&
                args.HitEntities.Count > 0 &&
                TryComp<MeleeWeaponComponent>(ent, out var melee) &&
                melee.HitSound != null)
            {
                _audio.PlayPredicted(melee.HitSound, args.HitEntities.First(), args.User);
            }

            return;
        }

        ApplyStamina(ent, args, ent.Comp.StaminaDamage);
    }

    private void ApplyStamina(Entity<WhipComponent> ent, MeleeHitEvent args, float amount)
    {
        if (!args.IsHit || amount <= 0f)
            return;

        var targets = new List<EntityUid>();
        foreach (var target in args.HitEntities)
        {
            if (HasComp<StaminaComponent>(target))
                targets.Add(target);
        }

        if (targets.Count == 0)
            return;

        // Mirror StaminaDamageOnHit: split the stamina damage between every eligible target.
        var damage = amount / targets.Count;
        foreach (var target in targets)
        {
            _stamina.TakeStaminaDamage(target, damage, source: args.User, with: args.Weapon);
        }
    }
}
