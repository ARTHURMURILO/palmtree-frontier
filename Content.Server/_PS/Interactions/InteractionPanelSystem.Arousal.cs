using Content.Server._PS.Organs; // Palmtree: womb overflow decals
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
            EmitClimax(uid, state, partner, proto);
    }

    /// <summary>How much semen each climax pulse emits.</summary>
    public const double ClimaxPulseVolume = 30;

    /// <summary>Hard cap on pulses per climax (even if the stored volume is larger).</summary>
    public const int MaxClimaxPulses = 10;

    /// <summary>Default delay between climax pulses (penis owners).</summary>
    private static readonly TimeSpan DefaultClimaxPulseInterval = TimeSpan.FromSeconds(0.6);

    /// <summary>Female climaxes pulse slower (1 second).</summary>
    private static readonly TimeSpan FemaleClimaxPulseInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Starts a climax: the actor's stored semen is split into <see cref="ClimaxPulseVolume"/>
    /// pulses (up to <see cref="MaxClimaxPulses"/>), each of which emits decals/drips, a cum
    /// message and a moan. The first pulse fires on the next update tick.
    /// </summary>
    private void EmitClimax(EntityUid uid, InteractionStateComponent state, EntityUid? partner, InteractionPrototype? proto)
    {
        var hasPenis = _organs.TryGetOrgan(uid, GenitalType.Penis, out _, out _);
        var hasVagina = _organs.TryGetOrgan(uid, GenitalType.Vagina, out _, out _);

        // Penis owners ejaculate; vagina owners squirt (SPLURT fem decals).
        if (!hasPenis && !hasVagina)
            return;

        var volume = _organs.GetFluidVolume(uid);
        if (volume <= 0)
            return;

        state.ClimaxPulseRemaining = volume;
        state.ClimaxPulsesLeft = Math.Clamp((int) Math.Ceiling(volume / ClimaxPulseVolume), 1, MaxClimaxPulses);
        state.ClimaxPulseInterval = hasPenis ? DefaultClimaxPulseInterval : FemaleClimaxPulseInterval;
        state.ClimaxPulseTarget = partner is { } partnerUid && !Deleted(partnerUid) ? partnerUid : null;
        state.ClimaxPulseProto = proto?.ID;
        state.ClimaxPulseFemale = !hasPenis;
        state.ClimaxPulseFirst = true;

        // Fire the first pulse immediately so the climax is responsive; the rest follow in Update.
        if (EmitClimaxPulse(uid, state, proto))
            state.ClimaxPulsesLeft--;
        else
            state.ClimaxPulsesLeft = 0;

        state.NextClimaxPulse = _timing.CurTime + state.ClimaxPulseInterval;

        if (state.ClimaxPulsesLeft <= 0)
        {
            state.ClimaxPulseRemaining = 0;
            state.ClimaxPulseTarget = null;
            state.ClimaxPulseProto = null;
            state.ClimaxPulseFirst = false;
            state.ClimaxPulseFemale = false;
        }
    }

    /// <summary>
    /// Fires a single climax pulse according to the interaction's
    /// <see cref="InteractionPrototype.CumTarget"/>: internal endings store the pulse in the
    /// partner (dripping when they have a vagina), exterior endings leave a full SPLURT cum decal.
    /// A null target (manual climax) is treated as exterior. Returns false when the sequence should
    /// stop (organ removed / nothing left to emit).
    /// </summary>
    private bool EmitClimaxPulse(EntityUid uid, InteractionStateComponent state, InteractionPrototype? proto)
    {
        var hasPenis = _organs.TryGetOrgan(uid, GenitalType.Penis, out _, out _);
        if (!hasPenis && !_organs.TryGetOrgan(uid, GenitalType.Vagina, out _, out _))
            return false;

        var amount = Math.Min(ClimaxPulseVolume, state.ClimaxPulseRemaining);
        if (amount <= 0)
            return false;

        state.ClimaxPulseRemaining -= amount;
        var first = state.ClimaxPulseFirst;
        state.ClimaxPulseFirst = false;

        if (state.ClimaxPulseFemale)
        {
            // Female squirt: a random fem decal lands under the actor, regardless of the
            // interaction's cum target (there is no penis to direct it anywhere).
            _drip.SpawnFemDecals(uid);
        }
        else
        {
            // Self/climax-with-no-target behaves like an exterior climax on the actor themselves.
            var cumTarget = proto?.CumTarget ?? "exterior";
            var target = state.ClimaxPulseTarget;
            var other = target is { } targetUid && !Deleted(targetUid) && targetUid != uid ? targetUid : uid;
            var hasPartner = other != uid;

            switch (cumTarget)
            {
                case "vagina":
                    if (!hasPartner || !_genitals.HasGenital(other, GenitalType.Vagina))
                        break;

                    // Palmtree: a womb that already holds more than 60u overflows, so the floor
                    // gets a full cum decal on every pulse. The 30u is still stored - overflow
                    // never costs the receiver anything.
                    if (_drip.GetSemen(other) > DrippingCumSystem.WombOverflowThreshold)
                        _drip.SpawnCumDecals(other, amount);

                    _drip.AddSemen(other, amount);
                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-inside");

                    if (first)
                    {
                        _popup.PopupEntity(Loc.GetString(_organs.IsWearingJumpsuit(other)
                            ? "interaction-cum-received-held"
                            : "interaction-cum-received"), other, other);
                    }

                    break;

                case "anus":
                    if (!hasPartner)
                        break;

                    _drip.AddSemen(other, amount);
                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-inside");

                    if (first)
                    {
                        _popup.PopupEntity(Loc.GetString(_organs.IsWearingJumpsuit(other)
                            ? "interaction-cum-received-held"
                            : "interaction-cum-received"), other, other);
                    }

                    break;

                case "mouth":
                    if (!hasPartner)
                        break;

                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-mouth");
                    break;

                case "exterior":
                default:
                    SendCumMessage(uid, other, proto, hasPartner, "interaction-cum-exterior");

                    // Ejaculation leaves the full cum decals; the drip reservoir only ever makes droplets.
                    _drip.SpawnCumDecals(other, amount);
                    break;
            }
        }

        // Each pulse comes with its own moan.
        var collection = GetSex(uid) == Sex.Female ? "LewdMoansFemale" : "LewdMoansMale";
        PlayInteractionSound(uid, new SoundCollectionSpecifier(collection), -8f, lewd: true);
        return true;
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
