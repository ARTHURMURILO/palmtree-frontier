// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Shared.Radiation.Components;
using Robust.Shared.Timing;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Handles self-decaying fuel rods.
/// </summary>
/// <remarks>
///     Ported from <c>fuel_rods.dm:83-84</c>. Only rods with <c>process = TRUE</c> in the original
///     (plutonium, depleted, and anything else that opts in) decay on their own:
///     <code>fuel_power = og_fuel_power * 0.5 ^ ((world.time - time_created) / half_life SECONDS)</code>
///     That halves the rod's output every <see cref="RbmkFuelRodComponent.HalfLife"/> seconds
///     <i>of world time</i>, whether or not the rod is installed in a reactor.
/// </remarks>
public sealed class RbmkFuelRodSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float DecayInterval = 1f;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RbmkFuelRodComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, RbmkFuelRodComponent rod, MapInitEvent args)
    {
        rod.SpawnedAt = _timing.CurTime;

        // The original overwrites fuel_power with og_fuel_power the moment a processing rod
        // first ticks, so the declared starting power of those subtypes is cosmetic.
        if (rod.SelfDecay)
            rod.FuelPower = rod.OriginalFuelPower;

        // fuel_rods.dm:27 - `AddComponent(/datum/component/radioactive, rad_strength, src)`.
        // RadStrength is already in SS14 units (rads per second at contact): the BYOND numbers in
        // the original are 500-6000, which is instantly fatal here.
        var source = EnsureComp<RadiationSourceComponent>(uid);
        source.Intensity = rod.RadStrength;
        source.Slope = 0.5f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;

        if (_accumulator < DecayInterval)
            return;

        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RbmkFuelRodComponent>();

        while (query.MoveNext(out _, out var rod))
        {
            if (!rod.SelfDecay || rod.HalfLife <= 0f)
                continue;

            var elapsed = (float) (now - rod.SpawnedAt).TotalSeconds;
            rod.FuelPower = rod.OriginalFuelPower * MathF.Pow(0.5f, elapsed / rod.HalfLife);
        }
    }
}
