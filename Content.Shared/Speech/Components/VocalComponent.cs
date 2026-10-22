using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared.Speech.Components;

/// <summary>
///     Component required for entities to be able to do vocal emotions.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class VocalComponent : Component
{
    /// <summary>
    ///     Emote sounds prototype id for each sex (not gender).
    ///     Entities without <see cref="HumanoidComponent"/> considered to be <see cref="Sex.Unsexed"/>.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public Dictionary<Sex, ProtoId<EmoteSoundsPrototype>>? Sounds;

    [DataField("screamId", customTypeSerializer: typeof(PrototypeIdSerializer<EmotePrototype>))]
    [AutoNetworkedField]
    public string ScreamId = "Scream";

    [DataField("wilhelm")]
    [AutoNetworkedField]
    public SoundSpecifier Wilhelm = new SoundPathSpecifier("/Audio/Voice/Human/wilhelm_scream.ogg");

    [DataField("wilhelmProbability")]
    [AutoNetworkedField]
    public float WilhelmProbability = 0.0002f;

    [DataField("screamAction", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    [AutoNetworkedField]
    public string? ScreamAction = "ActionScream";

    [DataField("screamActionEntity")]
    [AutoNetworkedField]
    public EntityUid? ScreamActionEntity;

    /// <summary>
    ///     Currently loaded emote sounds prototype, based on entity sex.
    ///     Null if no valid prototype for entity sex was found.
    /// </summary>
    [ViewVariables]
    [AutoNetworkedField]
    public ProtoId<EmoteSoundsPrototype>? EmoteSounds = null;

    /// <summary>
    /// Palmtree/Coyote: extra emote sounds shared by every vocal entity. Used for
    /// cross-species emotes (e.g. a vulpkanin barking or a felinid meowing) so the
    /// player-chosen emote categories still have sounds regardless of species.
    /// </summary>
    [ViewVariables]
    [AutoNetworkedField]
    public ProtoId<EmoteSoundsPrototype> SupplementalSounds = "SupplementalCoyoteEmoteStuff";

    /// <summary>
    /// Palmtree: optional per-sex supplemental sounds, checked before <see cref="SupplementalSounds"/>.
    /// Used for meme emotes with separate male/female voice lines (the Ace Attorney set).
    /// Server-only; the client never needs to resolve this set.
    /// </summary>
    [DataField]
    [ViewVariables]
    public Dictionary<Sex, ProtoId<EmoteSoundsPrototype>>? SexSupplementalSounds = new()
    {
        { Sex.Male, "SupplementalFunnyMale" },
        { Sex.Female, "SupplementalFunnyFemale" },
    };
}
