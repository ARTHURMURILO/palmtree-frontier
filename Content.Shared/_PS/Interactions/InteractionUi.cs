using Content.Shared._PS.Organs;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Interactions;

/// <summary>
/// Bound user interface key for the interaction panel. The panel opens on the actor
/// (each actor has their own panel bound to themselves) and carries the target in its state.
/// </summary>
[NetSerializable, Serializable]
public enum InteractionUiKey : byte
{
    Key,
}

/// <summary>One row in the interaction list.</summary>
[Serializable, NetSerializable]
public sealed class InteractionUiEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public InteractionType Type = InteractionType.Normal;

    /// <summary>Whether the interaction currently passes all requirement checks.</summary>
    public bool Available;

    /// <summary>Whether the user has this interaction favorited this session.</summary>
    public bool Favorite;
}

/// <summary>One row in the genital options tab (backed by a genital organ).</summary>
[Serializable, NetSerializable]
public sealed class InteractionGenitalUiEntry
{
    public GenitalType Type = GenitalType.Penis;
    public string Name = string.Empty;

    /// <summary>Localized size label, e.g. "Size 3" or "C Cup".</summary>
    public string SizeName = string.Empty;

    /// <summary>Whether the organ currently uses its aroused render marking.</summary>
    public bool Aroused;

    /// <summary>SPLURT-style exposure rule for this organ.</summary>
    public GenitalVisibility Visibility = GenitalVisibility.HiddenByUnderwear;

    /// <summary>Whether this panel may toggle the arousal state (only for your own organs).</summary>
    public bool CanToggleArousal;
}

/// <summary>State for the interaction panel.</summary>
[Serializable, NetSerializable]
public sealed class InteractionBoundUserInterfaceState : BoundUserInterfaceState
{
    public NetEntity Target;
    public string TargetName = string.Empty;
    public bool TargetIsSelf;

    public List<string> SelfAttributes = new();
    public List<string> TargetAttributes = new();

    public double Lust;
    public double MaxLust;
    public double TargetLust;
    public double TargetMaxLust;

    public List<InteractionUiEntry> Interactions = new();
    public List<InteractionGenitalUiEntry> Genitals = new();

    public bool Consent = true;
    public bool LewdSounds = true;

    public bool UseArousalMultiplier;
    public double ArousalMultiplier = 100;
    public bool UseMoaningMultiplier;
    public double MoaningMultiplier = 25;

    /// <summary>Palmtree: character draw-depth relative to the normal mob layer.</summary>
    public CharacterLayerMode LayerMode = CharacterLayerMode.Default;

    public string? ActiveAutoInteraction;
    public float AutoPace = 1f;

    public List<float> Speeds = new();
}

/// <summary>Perform an interaction once (or toggle auto-repeat when <see cref="Auto"/> is set).</summary>
[NetSerializable, Serializable]
public sealed class InteractionSelectedMessage(string interactionId, bool auto = false) : BoundUserInterfaceMessage
{
    public readonly string InteractionId = interactionId;
    public readonly bool Auto = auto;
}

/// <summary>Start or stop automatically repeating an interaction.</summary>
[NetSerializable, Serializable]
public sealed class InteractionToggleAutoMessage(string interactionId) : BoundUserInterfaceMessage
{
    public readonly string InteractionId = interactionId;
}

/// <summary>Change the auto-repeat pace (seconds between repeats).</summary>
[NetSerializable, Serializable]
public sealed class InteractionSetPaceMessage(float pace) : BoundUserInterfaceMessage
{
    public readonly float Pace = pace;
}

/// <summary>Toggle favorite status for an interaction (session only).</summary>
[NetSerializable, Serializable]
public sealed class InteractionToggleFavoriteMessage(string interactionId) : BoundUserInterfaceMessage
{
    public readonly string InteractionId = interactionId;
}

/// <summary>Toggle the arousal state of one of the actor's genital organs.</summary>
[NetSerializable, Serializable]
public sealed class InteractionGenitalArouseMessage(GenitalType type, bool aroused) : BoundUserInterfaceMessage
{
    public readonly GenitalType Type = type;
    public readonly bool Aroused = aroused;
}

/// <summary>Change the SPLURT-style visibility rule of one of the actor's genital organs.</summary>
[NetSerializable, Serializable]
public sealed class InteractionGenitalSetVisibilityMessage(GenitalType type, GenitalVisibility visibility) : BoundUserInterfaceMessage
{
    public readonly GenitalType Type = type;
    public readonly GenitalVisibility Visibility = visibility;
}

/// <summary>Toggle the actor's session interaction consent.</summary>
[NetSerializable, Serializable]
public sealed class InteractionSetConsentMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

/// <summary>Toggle whether the actor hears lewd interaction sounds.</summary>
[NetSerializable, Serializable]
public sealed class InteractionSetSoundsMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

/// <summary>Set the custom lust-gain multiplier preference.</summary>
[NetSerializable, Serializable]
public sealed class InteractionSetArousalMultiplierMessage(bool use, double value) : BoundUserInterfaceMessage
{
    public readonly bool Use = use;
    public readonly double Value = value;
}

/// <summary>Set the custom moaning chance preference.</summary>
[NetSerializable, Serializable]
public sealed class InteractionSetMoaningMultiplierMessage(bool use, double value) : BoundUserInterfaceMessage
{
    public readonly bool Use = use;
    public readonly double Value = value;
}

/// <summary>Set the character's draw-depth preference (below/default/above mobs).</summary>
[NetSerializable, Serializable]
public sealed class InteractionSetLayerModeMessage(CharacterLayerMode mode) : BoundUserInterfaceMessage
{
    public readonly CharacterLayerMode Mode = mode;
}
