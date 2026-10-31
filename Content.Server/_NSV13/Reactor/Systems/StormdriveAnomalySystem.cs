// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NSV13.Reactor.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Radiation.Components;
using Content.Server.Radiation.Systems;
using Content.Server.Stunnable;
using Content.Server.Emp;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NSV13.Reactor.Systems;

/// <summary>
///     Runs the three stormdrive anomalies: surge (EMP), sheer (radiation) and squall (gravity shear).
/// </summary>
/// <remarks>
///     Every pulse is a direct transcription of the corresponding <c>anomalyEffect()</c>, including
///     the 90 %/10 % split between the mild and the violent variant. On expiry each one detonates
///     the way its DM <c>detonate()</c> did.
/// </remarks>
public sealed class StormdriveAnomalySystem : EntitySystem
{
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly RadiationSystem _radiation = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<StormdriveAnomalyComponent>();

        while (query.MoveNext(out var uid, out var anomaly))
        {
            if (anomaly.SpawnedAt == TimeSpan.Zero)
                anomaly.SpawnedAt = now;

            if (now >= anomaly.SpawnedAt + anomaly.Lifespan)
            {
                Detonate(uid, anomaly);
                continue;
            }

            if (now < anomaly.NextPulse)
                continue;

            anomaly.NextPulse = now + anomaly.PulseInterval;
            Pulse(uid, anomaly);
        }
    }

    private void Pulse(EntityUid uid, StormdriveAnomalyComponent anomaly)
    {
        var coords = _transform.GetMapCoordinates(uid);

        switch (anomaly.Kind)
        {
            case StormdriveAnomalyKind.Surge:
                if (_random.Prob(0.9f))
                {
                    _emp.EmpPulse(coords, anomaly.SurgeEmpRange, 3f, 2f);
                    ShockMobs(coords, 0.5f, 20f);
                }
                else
                {
                    _emp.EmpPulse(coords, anomaly.SurgeEmpRange + 1f, 5f, 2f);
                    ShockMobs(coords, 2f, 20f);
                }

                break;

            case StormdriveAnomalyKind.Sheer:
                if (_random.Prob(0.9f))
                {
                    Irradiate(coords, anomaly.SheerRadiation);
                }
                else
                {
                    Irradiate(coords, anomaly.SheerRadiation * 5f);
                    // BRANCH NOTE (stormdrive branch): nucleium is a reactor gas.
                    // SpawnGas(uid, Gas.Nucleium, 15f);
                }

                break;

            case StormdriveAnomalyKind.Squall:
                if (_random.Prob(0.9f))
                {
                    // DM: "Add a little extra oxygenated air".
                    SpawnGas(uid, Gas.Oxygen, 5f);
                    SpawnGas(uid, Gas.Nitrogen, 15f);
                }
                else
                {
                    ThrowEverything(coords, anomaly.SquallRange);
                }

                break;
        }
    }

    private void Detonate(EntityUid uid, StormdriveAnomalyComponent anomaly)
    {
        var coords = _transform.GetMapCoordinates(uid);
        _audio.PlayPvs("/Audio/Effects/explosion1.ogg", uid);

        // DM rewrote the tile to 5000 K on detonation; the closed-form equivalent here is to heat
        // the mixture directly after the gas is added.
        anomaly.DetonationTemperature = 5000f;

        _explosion.QueueExplosion(uid,
            "Default",
            _explosion.RadiusToIntensity(2f, 1f, 1f),
            1f,
            1f,
            canCreateVacuum: false);

        switch (anomaly.Kind)
        {
            case StormdriveAnomalyKind.Surge:
                _emp.EmpPulse(coords, anomaly.SurgeEmpRange + 2f, 10f, 3f);
                ShockMobs(coords, 5f, 20f);
                break;

            case StormdriveAnomalyKind.Sheer:
                Irradiate(coords, anomaly.SheerRadiation * 200f);
                SpawnGas(uid, Gas.Oxygen, 125f);
                SpawnGas(uid, Gas.Plasma, 50f);
                // BRANCH NOTE (stormdrive branch): nucleium is a reactor gas.
                // SpawnGas(uid, Gas.Nucleium, 50f);
                break;

            case StormdriveAnomalyKind.Squall:
                ThrowEverything(coords, 6f);
                SpawnGas(uid, Gas.Oxygen, 200f);
                SpawnGas(uid, Gas.Nitrogen, 200f);
                break;
        }

        if (_atmosphere.GetTileMixture(uid, excite: true) is { } mix)
            mix.Temperature = anomaly.DetonationTemperature;

        Spawn("EffectSparks", coords);

        QueueDel(uid);
    }

    private void ShockMobs(MapCoordinates coords, float range, float damage)
    {
        foreach (var entity in _lookup.GetEntitiesInRange(coords.MapId, coords.Position, MathF.Max(range, 0.1f)))
        {
            // A storm surge shocks people, not doors and machinery: the DM original iterated
            // /mob/living. Without this filter TryChangeDamage happily burns every wall and console
            // in the radius.
            if (!HasComp<MobStateComponent>(entity) ||
                !TryComp<DamageableComponent>(entity, out var damageable))
            {
                continue;
            }

            _damageable.TryChangeDamage(entity,
                new DamageSpecifier { DamageDict = { ["Heat"] = damage } },
                damageable: damageable);

            TryComp<CrawlerComponent>(entity, out var crawler);
            _stun.TryKnockdown((entity, crawler), TimeSpan.FromSeconds(2), force: true);
        }
    }

    private void Irradiate(MapCoordinates coords, float rads)
    {
        foreach (var entity in _lookup.GetEntitiesInRange(coords.MapId, coords.Position, 1.5f))
        {
            if (!HasComp<RadiationReceiverComponent>(entity))
                continue;

            // IrradiateEntity takes rads/s and a duration; a pulse is modelled as one second's worth.
            _radiation.IrradiateEntity(entity, rads, 1f);
        }
    }

    private void ThrowEverything(MapCoordinates coords, float range)
    {
        foreach (var entity in _lookup.GetEntitiesInRange(coords.MapId, coords.Position, range))
        {
            if (!TryComp<PhysicsComponent>(entity, out var physics) || physics.BodyType != BodyType.Dynamic)
                continue;

            var offset = _transform.GetWorldPosition(entity) - coords.Position;
            if (offset.LengthSquared() < 0.01f)
                offset = _random.NextAngle().ToVec();

            _throwing.TryThrow(entity, offset.Normalized() * range, 4f, compensateFriction: true);
        }
    }

    /// <summary>
    ///     DM's <c>atmos_spawn_air()</c>: drop gas straight into the tile the anomaly is standing
    ///     on. Resolved from the entity's own transform, which is what <c>get_turf(src)</c> did.
    /// </summary>
    private void SpawnGas(EntityUid uid, Gas gas, float moles)
    {
        var mix = _atmosphere.GetTileMixture(uid, excite: true);
        mix?.AdjustMoles(gas, moles);
    }
}
