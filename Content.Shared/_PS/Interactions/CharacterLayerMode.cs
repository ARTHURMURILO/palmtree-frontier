using Robust.Shared.Serialization;

namespace Content.Shared._PS.Interactions;

/// <summary>
/// Palmtree: how a character's mob sprite is ordered relative to the normal character draw depth.
/// </summary>
[Serializable, NetSerializable]
public enum CharacterLayerMode : byte
{
    /// <summary>Draw below the normal mob layer.</summary>
    Below,

    /// <summary>Use the normal mob layer.</summary>
    Default,

    /// <summary>Draw above the normal mob layer.</summary>
    Above,
}
