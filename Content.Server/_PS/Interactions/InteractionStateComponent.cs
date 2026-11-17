using System.Collections.Generic;
using Robust.Shared.GameStates;

namespace Content.Server._PS.Interactions;

/// <summary>
/// Per-mob runtime state for the interaction panel: session preferences, arousal ("lust"),
/// auto-repeat bookkeeping and the current panel target.
/// </summary>
/// <remarks>
/// Everything here is session-only by design; the fork has no consent/profile storage for ERP.
/// Sandstorm's refractory period and pregnancy are intentionally not ported.
/// </remarks>
[RegisterComponent]
public sealed partial class InteractionStateComponent : Component
{
    #region Panel

    /// <summary>Entity the player currently has the panel open on.</summary>
    public EntityUid? Target;

    #endregion

    #region Preferences

    /// <summary>Whether other players may perform lewd interactions on this mob. Defaults to on.</summary>
    public bool Consent = true;

    /// <summary>Whether this player hears lewd interaction sounds.</summary>
    public bool LewdSounds = true;

    /// <summary>Session-only favorite interaction ids (Sandstorm stores these in prefs).</summary>
    public readonly List<string> Favorites = new();

    public bool UseArousalMultiplier;
    public double ArousalMultiplier = 100;

    public bool UseMoaningMultiplier;
    public double MoaningMultiplier = 25;

    #endregion

    #region Arousal

    /// <summary>Pleasure meter. Decays passively, increases through lewd interactions.</summary>
    public double Lust;

    /// <summary>When <see cref="Lust"/> was last read/updated, used for passive decay.</summary>
    public TimeSpan LastLustUpdate;

    /// <summary>Passive decay rate in points per second (Sandstorm: 1/s).</summary>
    public const double LustDecayPerSecond = 1.0;

    /// <summary>Climax threshold is <see cref="LustTolerance"/> * 3 (Sandstorm).</summary>
    public double LustTolerance;

    /// <summary>Randomized sexual potency; kept for parity with Sandstorm's arousal data.</summary>
    public int SexualPotency;

    /// <summary>Number of orgasms so far. Retained for future multi-orgasm content.</summary>
    public int Orgasms;

    /// <summary>Randomized on first use so players do not all climax at the same rate.</summary>
    public bool ArousalInitialized;

    #endregion

    #region Cooldowns / continuity

    /// <summary>Last time this mob performed any interaction (Sandstorm's last_interaction_time).</summary>
    public TimeSpan LastInteractionTime = TimeSpan.MinValue;

    /// <summary>Last interaction performed, used to pick "continuing" messages.</summary>
    public string? LastInteractionId;
    public EntityUid? LastInteractionTarget;
    public TimeSpan LastInteractionRepeat;

    #endregion

    #region Auto repeat

    public string? ActiveAutoInteraction;
    public EntityUid? AutoTarget;
    public float AutoPace = 1f;
    public TimeSpan NextAutoInteraction;

    #endregion
}
