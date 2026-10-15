using Content.Shared._PS.Consent;

namespace Content.Client._PS.Consent;

/// <summary>
/// Palmtree: client-side view of the local player's NSFW age gate. Fed by
/// <c>ClientPreferencesManager</c> whenever the server pushes preferences. All client-side NSFW
/// gates (genital sprites, semen decals, the genitals editor tab, the consent prompt) read this.
/// </summary>
/// <remarks>
/// Defaults to <see cref="NsfwConsentState.Unanswered"/> (not consenting) so nothing NSFW is
/// shown before the server data loads.
/// </remarks>
public static class NsfwGate
{
    public static NsfwConsentState State { get; private set; } = NsfwConsentState.Unanswered;

    /// <summary>True only when the player explicitly enabled NSFW features.</summary>
    public static bool Allowed => State == NsfwConsentState.Allowed;

    /// <summary>
    /// Raised when <see cref="State"/> changes. Used to rebuild sprite layers (genital markings
    /// are skipped at apply time, so existing sprites must be refreshed when the flag flips).
    /// </summary>
    public static event Action? Changed;

    public static void SetState(NsfwConsentState state)
    {
        if (State == state)
            return;

        State = state;
        Changed?.Invoke();
    }
}
