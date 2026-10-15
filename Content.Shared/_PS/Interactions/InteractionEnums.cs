using Robust.Shared.Serialization;

namespace Content.Shared._PS.Interactions;

/// <summary>
/// Broad classification of an interaction. Matches Sandstorm's normal/lewd split.
/// </summary>
[Serializable, NetSerializable]
public enum InteractionType : byte
{
    /// <summary>Social, non-sexual interaction.</summary>
    Normal = 0,

    /// <summary>Sexual interaction, gated on both parties' interaction consent.</summary>
    Lewd = 1,
}

/// <summary>
/// Flags describing the canonicity of an interaction (distance, self-targeting, consent).
/// </summary>
[Flags]
public enum InteractionFlags : uint
{
    None = 0,

    /// <summary>Both parties must be adjacent (obstructed range check).</summary>
    Adjacent = 1 << 0,

    /// <summary>Can only be used on yourself.</summary>
    UserIsTarget = 1 << 1,

    /// <summary>Both parties must have interaction consent enabled (lewd interactions).</summary>
    OocConsent = 1 << 2,

    /// <summary>
    /// Can be used on yourself and on others. Overrides the self/other exclusivity of
    /// <see cref="UserIsTarget"/>; used by "any time" actions like the manual Climax.
    /// </summary>
    SelfOrOther = 1 << 3,
}

/// <summary>
/// Body/genital requirements an interaction places on a participant.
/// "Exposed"/"unexposed" refer to genital markings being shown or hidden through
/// <c>ModifyUndies</c>; the anus is simulated from clothing like it is on Sandstorm.
/// </summary>
[Flags]
public enum InteractionRequirements : uint
{
    None = 0,

    Mouth = 1 << 0,
    Hands = 1 << 1,

    PenisExposed = 1 << 2,
    PenisUnexposed = 1 << 3,
    VaginaExposed = 1 << 4,
    VaginaUnexposed = 1 << 5,
    BreastsExposed = 1 << 6,
    BreastsUnexposed = 1 << 7,
    BallsExposed = 1 << 8,
    BallsUnexposed = 1 << 9,
    AnusExposed = 1 << 10,
    AnusUnexposed = 1 << 11,

    /// <summary>Any of the exposure-capable genitals (penis, balls, vagina, breasts, anus) is exposed.</summary>
    AnyGenitalExposed = 1 << 12,

    /// <summary>Any state (exposed or not) is acceptable for this genital.</summary>
    AnyPenis = PenisExposed | PenisUnexposed,
    AnyVagina = VaginaExposed | VaginaUnexposed,
    AnyBreasts = BreastsExposed | BreastsUnexposed,
    AnyBalls = BallsExposed | BallsUnexposed,
    AnyAnus = AnusExposed | AnusUnexposed,
}

/// <summary>
/// Genital kinds supported by the interaction system. Resolved from spawned
/// <see cref="Content.Shared._PS.Organs.GenitalOrganComponent"/> organs (which replaced the old
/// marking-based genitals), so the interaction logic does not touch the lookup itself.
/// </summary>
[Serializable, NetSerializable]
public enum GenitalType : byte
{
    Penis,
    Balls,
    Vagina,
    Breasts,
    Anus,
    Butt,
    Belly,
}

/// <summary>
/// Exposure state of a genital for interaction requirement checks.
/// </summary>
public enum GenitalExposure : byte
{
    /// <summary>The participant does not have this genital.</summary>
    None = 0,

    /// <summary>The genital exists but is hidden (marking not visible / hidden by clothing).</summary>
    Unexposed = 1,

    /// <summary>The genital exists and is exposed.</summary>
    Exposed = 2,
}
