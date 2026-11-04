using Content.Server.Actions;
using Content.Server.Chat.Systems;
using Content.Server.Speech.Components;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Cloning.Events;
using Content.Shared.Humanoid;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Speech.EntitySystems;

public sealed class VocalSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VocalComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VocalComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VocalComponent, SexChangedEvent>(OnSexChanged);
        SubscribeLocalEvent<VocalComponent, EmoteEvent>(OnEmote);
        SubscribeLocalEvent<VocalComponent, ScreamActionEvent>(OnScreamAction);
    }

    /// <summary>
    /// Copy this component's datafields from one entity to another.
    /// This can't use CopyComp because of the ScreamActionEntity DataField, which should not be copied.
    /// <summary>
    public void CopyComponent(Entity<VocalComponent?> source, EntityUid target)
    {
        if (!Resolve(source, ref source.Comp))
            return;

        var targetComp = EnsureComp<VocalComponent>(target);
        targetComp.Sounds = source.Comp.Sounds;
        targetComp.ScreamId = source.Comp.ScreamId;
        targetComp.Wilhelm = source.Comp.Wilhelm;
        targetComp.WilhelmProbability = source.Comp.WilhelmProbability;
        targetComp.SupplementalSounds = source.Comp.SupplementalSounds; // Palmtree/Coyote
        targetComp.SexSupplementalSounds = source.Comp.SexSupplementalSounds; // Palmtree
        LoadSounds(target, targetComp);

        Dirty(target, targetComp);
    }

    private void OnMapInit(EntityUid uid, VocalComponent component, MapInitEvent args)
    {
        // try to add scream action when vocal comp added
        _actions.AddAction(uid, ref component.ScreamActionEntity, component.ScreamAction);
        LoadSounds(uid, component);
    }

    private void OnShutdown(EntityUid uid, VocalComponent component, ComponentShutdown args)
    {
        // remove scream action when component removed
        if (component.ScreamActionEntity != null)
        {
            _actions.RemoveAction(uid, component.ScreamActionEntity);
        }
    }

    private void OnSexChanged(EntityUid uid, VocalComponent component, SexChangedEvent args)
    {
        LoadSounds(uid, component, args.NewSex);
    }

    private void OnEmote(EntityUid uid, VocalComponent component, ref EmoteEvent args)
    {
        if (args.Handled)
            return;

        // snowflake case for wilhelm scream easter egg
        if (args.Emote.ID == component.ScreamId)
        {
            args.Handled = TryPlayScreamSound(uid, component);
            return;
        }

        // Palmtree/Coyote: no category gate - species/cross-species emotes can play sounds too.
        EmoteSoundsPrototype? sounds = null;
        if (component.EmoteSounds is { } soundsId && _proto.TryIndex(soundsId, out var soundsProto))
            sounds = soundsProto;

        // Palmtree: gender-specific supplemental sounds take priority over the shared set.
        // This lets the Ace Attorney emotes use their male/female voice lines for every species.
        if (TryGetSexSupplemental(uid, component, out var sexSupplemental)
            && _chat.TryPlayEmoteSound(uid, sounds, sexSupplemental, args.Emote))
        {
            args.Handled = true;
            return;
        }

        args.Handled = _chat.TryPlayEmoteSound(uid, sounds, component.SupplementalSounds, args.Emote);
    }

    private bool TryGetSexSupplemental(
        EntityUid uid,
        VocalComponent component,
        out ProtoId<EmoteSoundsPrototype> supplemental)
    {
        supplemental = default;
        if (component.SexSupplementalSounds == null)
            return false;

        var sex = CompOrNull<HumanoidAppearanceComponent>(uid)?.Sex ?? Sex.Unsexed;
        return component.SexSupplementalSounds.TryGetValue(sex, out supplemental);
    }

    private void OnScreamAction(EntityUid uid, VocalComponent component, ScreamActionEvent args)
    {
        if (args.Handled)
            return;

        _chat.TryEmoteWithChat(uid, component.ScreamId);
        args.Handled = true;
    }

    private bool TryPlayScreamSound(EntityUid uid, VocalComponent component)
    {
        if (_random.Prob(component.WilhelmProbability))
        {
            _audio.PlayPvs(component.Wilhelm, uid, component.Wilhelm.Params);
            return true;
        }

        EmoteSoundsPrototype? sounds = null;
        if (component.EmoteSounds is { } soundsId && _proto.TryIndex(soundsId, out var soundsProto))
            sounds = soundsProto;

        return _chat.TryPlayEmoteSound(uid, sounds, component.SupplementalSounds, component.ScreamId);
    }

    private void LoadSounds(EntityUid uid, VocalComponent component, Sex? sex = null)
    {
        if (component.Sounds == null)
            return;

        sex ??= CompOrNull<HumanoidAppearanceComponent>(uid)?.Sex ?? Sex.Unsexed;

        if (!component.Sounds.TryGetValue(sex.Value, out var protoId))
            return;

        if (!_proto.HasIndex(protoId))
            return;

        component.EmoteSounds = protoId;
    }
}
