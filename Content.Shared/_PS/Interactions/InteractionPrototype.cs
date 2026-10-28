using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Interactions;

/// <summary>
/// Data-driven definition of a single entry in the interaction panel.
/// This is the SS14 equivalent of Sandstorm's <c>/datum/interaction</c> subtypes.
/// </summary>
/// <remarks>
/// Messages are locale keys. Both parties' names are passed to the locale as
/// <c>user</c> and <c>target</c>, e.g. <c>interaction-kiss-message = gives {target} a lingering kiss.</c>.
/// The chat system prefixes the user's name, so messages should not include it.
/// Requirement/flag lists contain names from <see cref="InteractionRequirements"/> and
/// <see cref="InteractionFlags"/> respectively (one name per entry).
/// </remarks>
[Prototype("interaction")]
public sealed partial class InteractionPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Locale key for the button label, e.g. "Kiss them deeply."</summary>
    [DataField("name", required: true)]
    public LocId Name { get; private set; }

    /// <summary>Whether the interaction is social or lewd. Lewd entries are consent-gated.</summary>
    [DataField("interactionType")]
    public InteractionType InteractionType { get; private set; } = InteractionType.Normal;

    /// <summary>
    /// Palmtree: marks a lewd interaction as non-sexual (e.g. kissing). Non-sexual entries skip
    /// the NSFW age gate: non-consenting bystanders still receive the emote/sound and
    /// non-consenting players can still be targeted by it. The voluntary per-session consent
    /// toggle and the "no panel until the prompt is answered" rule still apply.
    /// </summary>
    [DataField]
    public bool NonSexual { get; private set; }

    /// <summary>Interaction behavior flags (<see cref="InteractionFlags"/> names).</summary>
    [DataField]
    public List<string> Flags { get; private set; } = new();

    /// <summary>Requirements placed on the initiator (<see cref="InteractionRequirements"/> names).</summary>
    [DataField]
    public List<string> UserRequirements { get; private set; } = new();

    /// <summary>Requirements placed on the target (<see cref="InteractionRequirements"/> names).</summary>
    [DataField]
    public List<string> TargetRequirements { get; private set; } = new();

    /// <summary>Messages used the first time (and after <see cref="ContinueTimeout"/>).</summary>
    [DataField]
    public List<LocId> Messages { get; private set; } = new();

    /// <summary>
    /// Messages used when the same interaction is repeated on the same target within
    /// <see cref="ContinueTimeout"/> (Sandstorm's "is_fucking" continuing text).
    /// Falls back to <see cref="Messages"/> when null or empty.
    /// </summary>
    [DataField]
    public List<LocId>? ContinueMessages { get; private set; }

    /// <summary>How long repeating the same interaction keeps using the continuing messages.</summary>
    [DataField]
    public TimeSpan ContinueTimeout { get; private set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional sound played to nearby players who allow lewd sounds.</summary>
    [DataField]
    public SoundSpecifier? Sound { get; private set; }

    /// <summary>Volume in dB for <see cref="Sound"/>.</summary>
    [DataField]
    public float Volume { get; private set; } = -5f;

    /// <summary>Minimum time between two interactions from the same user.</summary>
    [DataField]
    public TimeSpan Cooldown { get; private set; } = TimeSpan.FromSeconds(0.5);

    /// <summary>Maximum distance between the two participants, in tiles.</summary>
    [DataField]
    public float MaxDistance { get; private set; } = 1.5f;

    /// <summary>
    /// Lust added to both parties. Doubled from Sandstorm's NORMAL_LUST (10) so actions progress
    /// faster on this fork.
    /// </summary>
    [DataField]
    public double Lust { get; private set; } = 20;

    /// <summary>
    /// If positive, both parties' lust is raised to at least this value instead of
    /// adding <see cref="Lust"/> (Sandstorm's kiss).
    /// </summary>
    [DataField]
    public double MinLust { get; private set; }

    /// <summary>
    /// Where the acting player's climax goes: <c>vagina</c>, <c>anus</c>, <c>mouth</c> or
    /// <c>exterior</c>. Null means the interaction does not emit fluid (or, for a
    /// <see cref="ForceClimax"/> action, means "reuse the last interaction's target").
    /// </summary>
    [DataField]
    public string? CumTarget { get; private set; }

    /// <summary>
    /// Messages used when the actor climaxes during this interaction, overriding the generic
    /// per-target cum text (SPLURT's context-sensitive finish lines). Both parties receive
    /// <c>{ $target }</c> as the other participant.
    /// </summary>
    [DataField]
    public List<LocId>? CumMessages { get; private set; }

    /// <summary>
    /// Immediately triggers the actor's climax (SPLURT's Climax verb and the manual "Cum on them").
    /// When the action has no <see cref="CumTarget"/> of its own and the last interaction on the
    /// same target happened recently, that interaction's cum target is used so a manual climax
    /// during sex still ends inside/on the partner.
    /// </summary>
    [DataField]
    public bool ForceClimax { get; private set; }

    /// <summary>
    /// Palmtree: removes the actor's stored semen (and its drip) when performed. Used by the
    /// "Clean yourself" action. The action does not arouse the actor or add lust.
    /// </summary>
    [DataField]
    public bool ClearsStoredCum { get; private set; }
}
