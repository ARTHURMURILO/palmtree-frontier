using Content.Server.Chat.Systems;
using Content.Shared._PS.Interactions;
using Content.Shared._PS.Organs;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Interactions;

public sealed partial class InteractionPanelSystem
{
    /// <summary>
    /// Gets (or creates) the runtime interaction state for a mob, randomizing arousal stats the
    /// first time. Sandstorm randomizes <c>lust_tolerance</c> (75-200) and <c>sexual_potency</c>
    /// (10-25) per mob; there is no character preference storage for them on this fork.
    /// </summary>
    public InteractionStateComponent EnsureState(EntityUid uid)
    {
        var state = EnsureComp<InteractionStateComponent>(uid);

        if (!state.ArousalInitialized)
        {
            state.ArousalInitialized = true;
            state.LustTolerance = _random.Next(75, 201);
            state.SexualPotency = _random.Next(10, 26);
            state.LastLustUpdate = _timing.CurTime;
        }

        return state;
    }

    /// <summary>Returns current lust after applying passive decay (Sandstorm: 1 point per second).</summary>
    public double GetLust(EntityUid uid, InteractionStateComponent state)
    {
        var now = _timing.CurTime;
        var elapsed = (now - state.LastLustUpdate).TotalSeconds;

        if (elapsed > 0)
        {
            state.Lust = Math.Max(0, state.Lust - elapsed * InteractionStateComponent.LustDecayPerSecond);
            state.LastLustUpdate = now;
        }

        return state.Lust;
    }

    public void SetLust(EntityUid uid, InteractionStateComponent state, double value)
    {
        state.Lust = Math.Max(0, value);
        state.LastLustUpdate = _timing.CurTime;
    }

    public void AddLust(EntityUid uid, InteractionStateComponent state, double amount, bool useMultiplier, double multiplier)
    {
        var current = GetLust(uid, state);
        var added = useMultiplier ? amount * multiplier / 100.0 : amount;
        SetLust(uid, state, current + added);
    }

    /// <summary>
    /// Applies Sandstorm's moan chance: either the player's custom chance, or a quadratic bezier
    /// curve between half and full lust tolerance.
    /// </summary>
    private double GetMoanChance(EntityUid uid, InteractionStateComponent state)
    {
        if (state.UseMoaningMultiplier)
            return Math.Clamp(state.MoaningMultiplier / 100.0, 0, 1);

        var lust = GetLust(uid, state);
        var threshold = state.LustTolerance * 1.5;
        var climax = state.LustTolerance * 3;

        if (lust < threshold)
            return 0;

        var t = Math.Clamp((lust - threshold) / (climax - threshold), 0, 1);
        var bezier = 2 * (1 - t) * t * 13.8 + t * t * 100;
        return Math.Clamp(bezier, 0, 100) / 100.0;
    }

    /// <summary>Rolls a moan (emote + gendered sound) for a participant.</summary>
    private void TryMoan(EntityUid uid, InteractionStateComponent state)
    {
        var lust = GetLust(uid, state);
        if (lust >= state.LustTolerance * 1.5 && _random.Prob(0.3f))
            _popup.PopupEntity(Loc.GetString("interaction-struggle"), uid, uid, PopupType.Medium);

        var chance = GetMoanChance(uid, state);
        if (chance <= 0 || !_random.Prob((float) chance))
            return;

        Moan(uid);
    }

    private void Moan(EntityUid uid)
    {
        SendErpEmote(uid, Loc.GetString("interaction-moan"));

        var collection = GetSex(uid) == Sex.Female ? "LewdMoansFemale" : "LewdMoansMale";
        PlayInteractionSound(uid, new SoundCollectionSpecifier(collection), -8f, lewd: true);
    }

    /// <summary>Triggers a climax (message + sound + fluid) when the threshold is crossed.</summary>
    private void TryClimax(EntityUid uid, InteractionStateComponent state, EntityUid? partner, InteractionPrototype? proto, bool isActor)
    {
        if (GetLust(uid, state) < state.LustTolerance * 3)
            return;

        TriggerClimax(uid, state, partner, proto, isActor);
    }

    /// <summary>Forces a climax regardless of the current lust value.</summary>
    private void TriggerClimax(EntityUid uid, InteractionStateComponent state, EntityUid? partner, InteractionPrototype? proto, bool isActor)
    {
        state.Orgasms++;
        SetLust(uid, state, 0);

        var message = partner is { } partnerUid
            ? Loc.GetString("interaction-climax-partner", ("target", Identity.Name(partnerUid, EntityManager)))
            : Loc.GetString("interaction-climax");

        SendErpEmote(uid, message);

        var collection = GetSex(uid) == Sex.Female ? "LewdClimaxFemale" : "LewdClimaxMale";
        PlayInteractionSound(uid, new SoundCollectionSpecifier(collection), -2f, lewd: true);

        // Only the acting party's climax maps to the interaction's cum target.
        if (isActor)
            EmitClimax(uid, partner, proto);
    }

    /// <summary>Male internal endings only spill a floor decal above this amount.</summary>
    private const double MaleOverflowThreshold = 50;

    /// <summary>
    /// Releases the actor's entire fluid volume at once according to the interaction's
    /// <see cref="InteractionPrototype.CumTarget"/>. Internal male endings store the whole amount in
    /// the partner and spill a single floor decal when the release is above 50u. Exterior endings
    /// and female climaxes each leave a single decal.
    /// </summary>
    private void EmitClimax(EntityUid uid, EntityUid? partner, InteractionPrototype? proto)
    {
        var hasPenis = _organs.TryGetOrgan(uid, GenitalType.Penis, out _, out _);
        var hasVagina = _organs.TryGetOrgan(uid, GenitalType.Vagina, out _, out _);

        // Penis owners ejaculate; vagina owners squirt (SPLURT fem decals).
        if (!hasPenis && !hasVagina)
            return;

        var amount = _organs.GetFluidVolume(uid);
        if (amount <= 0)
            return;

        if (!hasPenis)
        {
            // Female squirt: a single random fem decal under the actor.
            _drip.SpawnFemDecals(uid);
        }
        else
        {
            // Self/climax-with-no-target behaves like an exterior climax on the actor themselves.
            var cumTarget = proto?.CumTarget ?? "exterior";
            var other = partner is { } partnerUid && !Deleted(partnerUid) && partnerUid != uid ? partnerUid : uid;
            var hasPartner = other != uid;

            switch (cumTarget)
            {
                case "vagina":
                    if (!hasPartner || !_genitals.HasGenital(other, GenitalType.Vagina))
                        break;

                    _drip.AddSemen(other, amount);
                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-inside");
                    _popup.PopupEntity(Loc.GetString(_organs.IsWearingJumpsuit(other)
                        ? "interaction-cum-received-held"
                        : "interaction-cum-received"), other, other);

                    // Only a release above 50u spills; overflow costs no stored fluid.
                    if (amount > MaleOverflowThreshold)
                        _drip.SpawnCumDecals(other, amount);

                    break;

                case "anus":
                    if (!hasPartner)
                        break;

                    _drip.AddSemen(other, amount);
                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-inside");
                    _popup.PopupEntity(Loc.GetString(_organs.IsWearingJumpsuit(other)
                        ? "interaction-cum-received-held"
                        : "interaction-cum-received"), other, other);

                    if (amount > MaleOverflowThreshold)
                        _drip.SpawnCumDecals(other, amount);

                    break;

                case "mouth":
                    if (!hasPartner)
                        break;

                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-mouth");
                    break;

                case "exterior":
                default:
                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-exterior");

                    // Ejaculation leaves a single full cum decal; the drip reservoir only ever makes droplets.
                    _drip.SpawnCumDecals(other, amount);
                    break;
            }
        }

        // One moan for the whole release.
        var collection = GetSex(uid) == Sex.Female ? "LewdMoansFemale" : "LewdMoansMale";
        PlayInteractionSound(uid, new SoundCollectionSpecifier(collection), -8f, lewd: true);
    }

    /// <summary>
    /// Sends the interaction's context-sensitive cum line when it defines one; otherwise falls
    /// back to the generic per-target line for partner climaxes (self climaxes stay quiet).
    /// </summary>
    private void SendCumMessage(EntityUid uid, EntityUid other, InteractionPrototype? proto, bool hasPartner, string fallback)
    {
        if (proto?.CumMessages is { Count: > 0 } pool)
        {
            SendErpEmote(uid, Loc.GetString(_random.Pick(pool), ("target", Identity.Name(other, EntityManager))));
            return;
        }

        if (hasPartner)
            SendErpEmote(uid, Loc.GetString(fallback, ("target", Identity.Name(other, EntityManager))));
    }

    private Sex GetSex(EntityUid uid)
    {
        return TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) ? humanoid.Sex : Sex.Male;
    }
}
