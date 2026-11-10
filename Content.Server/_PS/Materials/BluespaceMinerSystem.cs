using Content.Server._PS.Materials.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Wires;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Materials;

/// <summary>
/// Palmtree: drives the bluespace miner. Every <see cref="BluespaceMinerComponent.SecondsPerCycle"/>
/// it picks one weighted ore and deposits it into the linked ore silo, or buffers it in the
/// machine's own storage when no silo is linked.
/// </summary>
public sealed class BluespaceMinerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly SharedOreSiloSystem _oreSilo = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BluespaceMinerComponent, MaterialStorageComponent>();
        while (query.MoveNext(out var uid, out var miner, out var storage))
        {
            if (!miner.Enabled)
                continue;

            if (TryComp<WiresPanelComponent>(uid, out var panel) && panel.Open)
                continue;

            if (!this.IsPowered(uid, EntityManager))
                continue;

            var now = _timing.CurTime;
            if (miner.NextMiningTime == TimeSpan.Zero)
            {
                miner.NextMiningTime = now + GetInterval(miner);
                continue;
            }

            if (now < miner.NextMiningTime)
                continue;

            miner.NextMiningTime = now + GetInterval(miner);

            if (miner.AmountPerCycle <= 0 || !TryPickOre(miner, out var material))
                continue;

            if (!_prototype.HasIndex(material))
                continue;

            Mine((uid, miner, storage), material);
        }
    }

    private void Mine(Entity<BluespaceMinerComponent, MaterialStorageComponent> ent, ProtoId<MaterialPrototype> material)
    {
        var amount = ent.Comp1.AmountPerCycle;

        if (TryComp<OreSiloClientComponent>(ent.Owner, out var client) &&
            client.Silo is { } silo &&
            _oreSilo.CanTransmitMaterials(silo, ent.Owner))
        {
            if (_materialStorage.TryChangeMaterialAmount(silo, material.Id, amount))
            {
                _audio.PlayPvs(ent.Comp1.MiningSound, ent.Owner);
                return;
            }
        }

        // No silo (or the silo cannot receive right now): keep the ore in the machine until it is
        // linked. Linking an OreSiloClient transfers its local storage into the silo.
        if (_materialStorage.TryChangeMaterialAmount(ent.Owner, material.Id, amount, ent.Comp2, localOnly: true))
            _audio.PlayPvs(ent.Comp1.MiningSound, ent.Owner);
    }

    /// <summary>
    /// Picks one material using the component's weights. Zero and negative weights are ignored so
    /// an admin can disable an entry through View Variables without breaking the table.
    /// </summary>
    private bool TryPickOre(BluespaceMinerComponent miner, out ProtoId<MaterialPrototype> material)
    {
        material = default;

        var totalWeight = 0f;
        foreach (var weight in miner.OreWeights.Values)
        {
            if (weight > 0f)
                totalWeight += weight;
        }

        if (totalWeight <= 0f)
            return false;

        var roll = _random.NextFloat() * totalWeight;
        foreach (var (mat, weight) in miner.OreWeights)
        {
            if (weight <= 0f)
                continue;

            roll -= weight;
            if (roll > 0f)
                continue;

            material = mat;
            return true;
        }

        return false;
    }

    private static TimeSpan GetInterval(BluespaceMinerComponent miner)
    {
        var seconds = miner.SecondsPerCycle;
        if (!float.IsFinite(seconds) || seconds <= 0f)
            seconds = 1f;

        // Keep a VV typo from asking for an interval outside TimeSpan's range.
        return TimeSpan.FromSeconds(Math.Min(seconds, 86400f));
    }
}
