// SPDX-FileCopyrightText: 2025 Steve <marlumpy@gmail.com>
// SPDX-FileCopyrightText: 2025 marc-pelletier <113944176+marc-pelletier@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.Radiation.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Server.Atmos.Reactions;

/// <summary>
///     Assmos - /tg/ gases
///     Consumes a tiny amount of tritium to convert CO2 and oxygen to pluoxium.
/// </summary>
/// <remarks>
///     Palmtree: this runs inside the atmos simulation, once per holder per tick. The old version
///     called EnsureComponent (and rewrote a timer) on every node of every pipe net on every tick,
///     and reported <see cref="ReactionResult.Reacting"/> even when the radiation check failed.
///     Receiver setup is now done for a holder once per <see cref="ReceiverRefreshInterval"/>, and
///     the state is reused from a per-holder cache on the ticks in between.
/// </remarks>
[UsedImplicitly]
public sealed partial class PluoxiumRadiationProductionReaction : IGasReactionEffect
{
    private IEntityManager entityManager => IoCManager.Resolve<IEntityManager>();
    private IGameTiming gameTiming => IoCManager.Resolve<IGameTiming>();

    private const float RadiationThreshold = 0.01f;
    private static readonly TimeSpan TimerDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often the cached radiation receivers are set up/refreshed. The radiation gridcast
    ///     itself only updates once a second, so there is nothing to gain from doing it every tick.
    /// </summary>
    private static readonly TimeSpan ReceiverRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PipeNet, ReceiverSet> _pipeNetReceivers = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IComponent, ReceiverSet> _entityReceivers = new();

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (holder == null)
            return ReactionResult.NoReaction;

        if (!TryGetRadiationLevel(holder, out var radiationLevel) ||
            !float.IsFinite(radiationLevel) ||
            radiationLevel < RadiationThreshold)
        {
            return ReactionResult.NoReaction;
        }

        float initO2 = mixture.GetMoles(Gas.Oxygen);
        float initCO2 = mixture.GetMoles(Gas.CarbonDioxide);

        float producedAmount = Math.Min(radiationLevel, Math.Min(initCO2, initO2 * 2f));
        if (!float.IsFinite(producedAmount) || producedAmount <= 0f)
            return ReactionResult.NoReaction;

        float co2Removed = producedAmount;
        float oxyRemoved = producedAmount * 0.5f;

        mixture.AdjustMoles(Gas.CarbonDioxide, -co2Removed);
        mixture.AdjustMoles(Gas.Oxygen, -oxyRemoved);
        mixture.AdjustMoles(Gas.Pluoxium, producedAmount);

        float energyReleased = producedAmount * Atmospherics.PluoxiumProductionEnergy;
        float heatCap = atmosphereSystem.GetHeatCapacity(mixture, true);
        if (float.IsFinite(heatCap) && heatCap > Atmospherics.MinimumHeatCapacity)
            mixture.Temperature = Math.Max((mixture.Temperature * heatCap + energyReleased) / heatCap, Atmospherics.TCMB);

        return ReactionResult.Reacting;
    }

    private bool TryGetRadiationLevel(IGasMixtureHolder holder, out float radiationLevel)
    {
        switch (holder)
        {
            case TileAtmosphere:
                // Our radiation system has no per-tile cache yet, so tiles count as unirradiated.
                radiationLevel = 0f;
                return false;
            case IComponent component:
                return TryGetEntityRadiation(component, out radiationLevel);
            case PipeNet pipeNet:
                return TryGetPipeNetRadiation(pipeNet, out radiationLevel);
            default:
                radiationLevel = 0f;
                return false;
        }
    }

    private bool TryGetEntityRadiation(IComponent holder, out float radiationLevel)
    {
        var set = _entityReceivers.GetValue(holder, static _ => new ReceiverSet());

        if (NeedsRefresh(set))
        {
            set.Entities.Clear();
            set.Entities.Add(holder.Owner);
            EnsureReceiver(holder.Owner);
            MarkRefreshed(set);
        }

        return TrySampleRadiation(set, average: false, out radiationLevel);
    }

    private bool TryGetPipeNetRadiation(PipeNet pipeNet, out float radiationLevel)
    {
        var set = _pipeNetReceivers.GetValue(pipeNet, static _ => new ReceiverSet());

        if (NeedsRefresh(set))
        {
            set.Entities.Clear();
            foreach (var node in pipeNet.Nodes)
            {
                set.Entities.Add(node.Owner);
                EnsureReceiver(node.Owner);
            }

            MarkRefreshed(set);
        }

        return TrySampleRadiation(set, average: true, out radiationLevel);
    }

    /// <summary>
    ///     Makes sure the entity can be sampled by the radiation system. The timer keeps the
    ///     receiver around for a few seconds after the reaction stops so it does not have to be
    ///     re-added every time a pipe net briefly stops reacting.
    /// </summary>
    private void EnsureReceiver(EntityUid uid)
    {
        var expiresAt = gameTiming.CurTime + TimerDuration;

        if (!entityManager.TryGetComponent<RadiationReceiverComponent>(uid, out _))
        {
            entityManager.EnsureComponent<RadiationReceiverComponent>(uid);

            var timer = entityManager.EnsureComponent<RadiationReceiverTimerComponent>(uid);
            timer.RemoveReceiver = true;
            timer.TimerExpiresAt = expiresAt;
            return;
        }

        if (entityManager.TryGetComponent<RadiationReceiverTimerComponent>(uid, out var existingTimer))
            existingTimer.TimerExpiresAt = expiresAt;
    }

    private bool TrySampleRadiation(ReceiverSet set, bool average, out float radiationLevel)
    {
        radiationLevel = 0f;
        var sampled = 0;

        foreach (var uid in set.Entities)
        {
            if (!entityManager.TryGetComponent<RadiationReceiverComponent>(uid, out var receiver) ||
                !float.IsFinite(receiver.CurrentRadiation))
            {
                continue;
            }

            radiationLevel += receiver.CurrentRadiation;
            sampled++;
        }

        if (sampled == 0)
            return false;

        if (average)
            radiationLevel /= sampled;

        return float.IsFinite(radiationLevel);
    }

    private bool NeedsRefresh(ReceiverSet set)
    {
        return !set.Initialized || gameTiming.CurTime >= set.NextRefresh;
    }

    private void MarkRefreshed(ReceiverSet set)
    {
        set.Initialized = true;
        set.NextRefresh = gameTiming.CurTime + ReceiverRefreshInterval;
    }

    /// <summary>
    ///     Radiation receivers that were set up for a given holder, reused between refreshes.
    /// </summary>
    private sealed class ReceiverSet
    {
        public readonly List<EntityUid> Entities = new();
        public bool Initialized;
        public TimeSpan NextRefresh;
    }
}

[RegisterComponent]
public sealed partial class RadiationReceiverTimerComponent : Component
{
    public TimeSpan TimerExpiresAt { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Whether this timer added the radiation receiver and should remove it again.
    /// </summary>
    public bool RemoveReceiver;
}

public sealed partial class RadiationTimerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEntityManager entityManager = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var timer in entityManager.EntityQuery<RadiationReceiverTimerComponent>())
        {
            var uid = timer.Owner;
            if (_timing.CurTime >= timer.TimerExpiresAt)
            {
                if (timer.RemoveReceiver)
                    entityManager.RemoveComponent<RadiationReceiverComponent>(uid);
                entityManager.RemoveComponent<RadiationReceiverTimerComponent>(uid);
            }
        }
    }
}
