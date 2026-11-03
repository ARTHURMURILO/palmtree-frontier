using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.RCD.Components;
using Robust.Shared.Timing;

namespace Content.Shared.RCD.Systems;

public sealed class RCDAmmoSystem : EntitySystem
{
    /// <summary>
    /// Palmtree: the RPD's 60-charge tank should take one standard 30-charge compressed-matter
    /// cartridge, so its cartridge charges are worth two tool charges each. The RCD stays 1:1.
    /// </summary>
    private const int RpdAmmoChargeMultiplier = 2;

    [Dependency] private readonly SharedChargesSystem _sharedCharges = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RCDAmmoComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<RCDAmmoComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnExamine(EntityUid uid, RCDAmmoComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var examineMessage = Loc.GetString("rcd-ammo-component-on-examine", ("charges", comp.Charges));
        args.PushText(examineMessage);
    }

    private void OnAfterInteract(EntityUid uid, RCDAmmoComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !_timing.IsFirstTimePredicted)
            return;

        if (args.Target is not { Valid: true } target ||
            !HasComp<RCDComponent>(target) ||
            !TryComp<LimitedChargesComponent>(target, out var charges))
            return;

        var current = _sharedCharges.GetCurrentCharges((target, charges));
        var user = args.User;

        // ## Frontier - Shipyard RCD ammo only fits in shipyard RCD.
        // At this point RCDComponent is guaranteed
        EnsureComp<RCDComponent>(target, out var rcdComponent);
        if (rcdComponent.IsShipyardRCD && !comp.IsShipyardRCDAmmo || !rcdComponent.IsShipyardRCD && comp.IsShipyardRCDAmmo)
        {
            _popup.PopupClient(Loc.GetString("rcd-component-wrong-ammo-type"), target, user);
            return;
        }

        args.Handled = true;

        // Palmtree: the RPD gets more tool charges out of the same cartridge, so one standard
        // compressed-matter cartridge fills its larger tank. The cartridge is consumed at the
        // normal rate: only as many of its charges as are needed to fill the tool are used.
        var multiplier = rcdComponent.IsRpd ? RpdAmmoChargeMultiplier : 1;
        var missing = charges.MaxCharges - current;
        var count = (int) Math.Min(missing, (long) comp.Charges * multiplier);
        if (count <= 0)
        {
            _popup.PopupClient(Loc.GetString("rcd-ammo-component-after-interact-full"), target, user);
            return;
        }

        var consumed = (int) (((long) count + multiplier - 1) / multiplier);

        _popup.PopupClient(Loc.GetString("rcd-ammo-component-after-interact-refilled"), target, user);
        _sharedCharges.AddCharges(target, count);
        comp.Charges -= consumed;
        Dirty(uid, comp);

        // prevent having useless ammo with 0 charges
        if (comp.Charges <= 0)
            QueueDel(uid);
    }
}
