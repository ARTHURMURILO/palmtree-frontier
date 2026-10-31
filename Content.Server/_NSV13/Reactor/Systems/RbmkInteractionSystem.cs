// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._NSV13.Reactor;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Server.Temperature.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.GameObjects;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Everything you can do to the RBMK with your hands: load fuel, patch the vessel, get told off
///     for standing on it, and grill your lunch.
/// </summary>
/// <remarks>
///     Ported from the <c>attackby</c>/<c>welder_act</c>/<c>examine</c>/<c>examine_more</c> and
///     walk-over branches of <c>rbmk.dm</c>. The `do_after` timings, power interlocks and integrity
///     gates are the original's.
/// </remarks>
public sealed class RbmkInteractionSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    /// <summary>§attackby: fuel cannot be touched once the core is above this power percentage.</summary>
    public const float MaxPowerForFuelHandling = 20f;

    private static readonly TimeSpan FuelInsertTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RepairTime = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RbmkReactorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<RbmkReactorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RbmkReactorComponent, RbmkFuelInsertDoAfterEvent>(OnFuelInserted);
        SubscribeLocalEvent<RbmkReactorComponent, RbmkSealantDoAfterEvent>(OnSealed);
    }

    private void OnInteractUsing(EntityUid uid, RbmkReactorComponent reactor, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<RbmkFuelRodComponent>(args.Used))
        {
            TryInsertFuel(uid, reactor, args);
            return;
        }

        if (HasComp<ReactorSealantComponent>(args.Used))
        {
            TrySeal(uid, reactor, args);
            return;
        }

        if (TryComp<WelderComponent>(args.Used, out _))
        {
            TryWeld(uid, reactor, args);
        }
    }

    private void TryInsertFuel(EntityUid uid, RbmkReactorComponent reactor, InteractUsingEvent args)
    {
        args.Handled = true;

        if (reactor.Power >= MaxPowerForFuelHandling)
        {
            _popup.PopupEntity(Loc.GetString("rbmk-fuel-too-hot"), uid, args.User);
            return;
        }

        if (reactor.FuelRods.Count >= reactor.MaxFuelRods)
        {
            _popup.PopupEntity(Loc.GetString("rbmk-fuel-full"), uid, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, FuelInsertTime,
            new RbmkFuelInsertDoAfterEvent(), uid, target: uid, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnFuelInserted(EntityUid uid, RbmkReactorComponent reactor, RbmkFuelInsertDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (args.Used is not { } rodUid || !Exists(rodUid) || !HasComp<RbmkFuelRodComponent>(rodUid))
            return;

        if (reactor.Power >= MaxPowerForFuelHandling || reactor.FuelRods.Count >= reactor.MaxFuelRods)
            return;

        args.Handled = true;

        // The rod entity has to survive: the tick reads its fuel power and depletion, and the fuel
        // console ejects it later. The DM original forceMove()d it into the machine's contents, so
        // hide it in a container the same way instead of deleting it (which used to silently empty
        // the core on the very next tick). Insert also registers the rod with the core and starts
        // the reaction if it was the first one - see RbmkReactorSystem.OnRodInserted.
        var container = _container.EnsureContainer<Container>(uid, RbmkReactorComponent.FuelContainer);

        if (!_container.Insert(rodUid, container))
            return;

        // rbmk.dm:730 - rods are slotted home with a switch click, not a welder hiss.
        if (reactor.SwitchSounds.Count > 0)
            _audio.PlayPvs(_random.Pick(reactor.SwitchSounds), uid);

        _popup.PopupEntity(Loc.GetString("rbmk-fuel-inserted"), uid, args.User);
    }

    private void TrySeal(EntityUid uid, RbmkReactorComponent reactor, InteractUsingEvent args)
    {
        args.Handled = true;

        if (reactor.Slagged)
        {
            _popup.PopupEntity(Loc.GetString("rbmk-repair-slagged"), uid, args.User);
            return;
        }

        if (reactor.Power >= MaxPowerForFuelHandling)
        {
            _popup.PopupEntity(Loc.GetString("rbmk-repair-power"), uid, args.User);
            return;
        }

        if (reactor.VesselIntegrity >= reactor.MaxVesselIntegrity * 0.875f)
        {
            _popup.PopupEntity(Loc.GetString("rbmk-repair-seals-intact"), uid, args.User);
            return;
        }

        if (reactor.VesselIntegrity <= reactor.MaxVesselIntegrity * 0.5f)
        {
            _popup.PopupEntity(Loc.GetString("rbmk-repair-needs-welder"), uid, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, RepairTime,
            new RbmkSealantDoAfterEvent(), uid, target: uid, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnSealed(EntityUid uid, RbmkReactorComponent reactor, RbmkSealantDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        // The DM original re-checks the same 350/400 gate after the do_after, so stacked do_afters
        // cannot push the seals past the point where sealant is supposed to stop working.
        if (reactor.VesselIntegrity >= reactor.MaxVesselIntegrity * 0.875f)
            return;

        reactor.VesselIntegrity = MathF.Min(reactor.VesselIntegrity + 10f, reactor.MaxVesselIntegrity);

        _audio.PlayPvs("/Audio/Effects/spray2.ogg", uid);
        _popup.PopupEntity(Loc.GetString("rbmk-repair-sealed"), uid, args.User);
    }

    private void TryWeld(EntityUid uid, RbmkReactorComponent reactor, InteractUsingEvent args)
    {
        if (reactor.Slagged)
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("rbmk-repair-slagged"), uid, args.User);
            return;
        }

        if (reactor.Power >= MaxPowerForFuelHandling)
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("rbmk-repair-power"), uid, args.User);
            return;
        }

        if (reactor.VesselIntegrity > reactor.MaxVesselIntegrity * 0.5f)
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("rbmk-repair-weld-free"), uid, args.User);
            return;
        }

        args.Handled = true;
        reactor.VesselIntegrity = MathF.Min(reactor.VesselIntegrity + 20f, reactor.MaxVesselIntegrity);

        _audio.PlayPvs("/Audio/Items/welder.ogg", uid);
        _popup.PopupEntity(Loc.GetString("rbmk-repair-welded"), uid, args.User);
    }

    /// <summary>
    ///     §<c>examine_more()</c>: a qualitative read on how healthy the vessel is. The DM original
    ///     described the seals in words rather than numbers, and only when you were close enough.
    /// </summary>
    private void OnExamined(EntityUid uid, RbmkReactorComponent reactor, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var percent = reactor.MaxVesselIntegrity <= 0f
            ? 0f
            : reactor.VesselIntegrity / reactor.MaxVesselIntegrity * 100f;

        var message = reactor.Slagged
            ? Loc.GetString("rbmk-examine-slagged")
            : percent switch
            {
                >= 95f => Loc.GetString("rbmk-examine-new"),
                >= 80f => Loc.GetString("rbmk-examine-good"),
                >= 50f => Loc.GetString("rbmk-examine-worn"),
                >= 20f => Loc.GetString("rbmk-examine-cracked"),
                _ => Loc.GetString("rbmk-examine-failing"),
            };

        args.PushMarkup(message);

        // The ported art has no visible pipe stubs and the machine only accepts pipes on its centre
        // tile, so the faces are spelled out instead of leaving the crew to guess.
        args.PushMarkup(Loc.GetString("rbmk-examine-ports"));
    }

    /// <summary>
    ///     §C.1 walk-over: standing on a hot core heats you up, and anything edible on it gets
    ///     cooked. Called from the reactor tick so it shares the 2 s cadence.
    /// </summary>
    public void ApplyContactEffects(EntityUid uid, RbmkReactorComponent reactor)
    {
        if (reactor.Temperature <= 0f)
            return;

        var coords = _transform.GetMapCoordinates(uid);

        foreach (var entity in _lookup.GetEntitiesInRange(coords.MapId, coords.Position, 0.5f))
        {
            if (entity == uid)
                continue;

            if (HasComp<TemperatureComponent>(entity))
            {
                // DM: adjust_bodytemperature(CLAMP(temperature, BODYTEMP_COOLING_MAX, BODYTEMP_HEATING_MAX)).
                // Degrees are converted to joules by the body's heat capacity, which the SS14
                // temperature system already knows how to do.
                var degrees = Math.Clamp(reactor.Temperature, -20f, 90f);
                _temperature.ChangeHeat(entity, degrees * 60f);
                continue;
            }

            if (reactor.Power < 20f || !HasComp<FoodComponent>(entity))
                continue;

            if (_random.Prob(0.8f))
                continue;

            // Cosmetic grilling tiers, straight out of the DM power switch. The DM rewrote the name
            // and description from initial() every time, so a hotter tier replaces the previous one
            // instead of stacking prefixes; the prototype's own name/description stand in for
            // initial() here.
            var (nameKey, descriptionKey) = reactor.Power switch
            {
                < 40f => ("rbmk-grilled-name-1", "rbmk-grilled-description-1"),
                < 70f => ("rbmk-grilled-name-2", "rbmk-grilled-description-2"),
                < 95f => ("rbmk-grilled-name-3", "rbmk-grilled-description-3"),
                _ => ("rbmk-grilled-name-4", "rbmk-grilled-description-4"),
            };

            var meta = MetaData(entity);
            var originalName = meta.EntityPrototype?.Name ?? meta.EntityName;
            var originalDescription = meta.EntityPrototype?.Description ?? meta.EntityDescription;

            var name = Loc.GetString(nameKey, ("name", originalName));
            var description = Loc.GetString(descriptionKey, ("description", originalDescription));

            if (meta.EntityName != name)
                _meta.SetEntityName(entity, name, meta);

            if (meta.EntityDescription != description)
                _meta.SetEntityDescription(entity, description, meta);

            _audio.PlayPvs("/Audio/Effects/zzzt.ogg", uid);
        }
    }
}
