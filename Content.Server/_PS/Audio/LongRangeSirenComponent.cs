namespace Content.Server._PS.Audio;

/// <summary>
/// Palmtree: turns an <see cref="Content.Shared.Audio.AmbientSoundComponent"/> into a long-range siren.
/// </summary>
/// <remarks>
/// The client ambient sampler only considers sounds within <c>ambience.range</c> (8 m by default), and
/// an entity's ambient stream stops when the entity leaves PVS. This component instead plays a
/// server-side looping stream and PVS-overrides it, so the siren stays loaded and audible far outside
/// normal view range. The ambient component remains the on/off switch (the powered light toggles it)
/// and the sound/volume source; keep its <c>range</c> at zero so the nearby ambient system does not
/// play a second copy of the same sound.
/// </remarks>
[RegisterComponent]
public sealed partial class LongRangeSirenComponent : Component
{
    /// <summary>Distance at which the siren becomes inaudible.</summary>
    [DataField]
    public float MaxDistance = 60f;

    /// <summary>Distance at which the siren plays at full volume.</summary>
    [DataField]
    public float ReferenceDistance = 8f;

    /// <summary>The active looping stream, if any.</summary>
    [ViewVariables]
    public EntityUid? Stream;
}
