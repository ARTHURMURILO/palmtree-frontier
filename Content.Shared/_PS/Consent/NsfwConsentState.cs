namespace Content.Shared._PS.Consent;

/// <summary>
/// Palmtree: account-level NSFW age gate state.
/// </summary>
/// <remarks>
/// Every player starts as <see cref="Unanswered"/> and is treated as non-consenting until they
/// answer the one-time lobby prompt. The answer is stored on the account; afterwards only the
/// <c>nsfwtoggle</c> console command can change it. Anything other than <see cref="Allowed"/>
/// must be treated as "no NSFW content", including when preferences have not loaded yet.
/// </remarks>
public enum NsfwConsentState : byte
{
    /// <summary>The player has not answered the consent prompt yet. Treated as not consenting.</summary>
    Unanswered = 0,

    /// <summary>The player answered that they do not wish to see NSFW features.</summary>
    Denied = 1,

    /// <summary>The player answered that they are 18+ and wish to see NSFW features.</summary>
    Allowed = 2,
}
