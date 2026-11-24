using System.Linq;
using System.Numerics;
using Content.Server._NF.Radio; // Frontier
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Humanoid;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Shared._CS.RadioNoises; // Palmtree/Coyote: Shortband radio
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Server.GameObjects; // Frontier
using Content.Shared.Speech;
using Content.Shared.Ghost; // Nuclear-14
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;
using Content.Server._NF.Radio.Components; // Frontier: add support for handheld radio frequency

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles intrinsic radios and the general process of converting radio messages into chat messages.
/// </summary>
public sealed partial class RadioSystem : EntitySystem
{
    [Dependency] private INetManager _netMan = default!;
    [Dependency] private IReplayRecordingManager _replay = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private TransformSystem _t = default!; // Palmtree/Coyote: Shortband radio
    [Dependency] private HumanoidAppearanceSystem _ham = default!; // Palmtree/Coyote: Shortband radio

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    private EntityQuery<TelecomExemptComponent> _exemptQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);

        _exemptQuery = GetEntityQuery<TelecomExemptComponent>();
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    //Nuclear-14
    /// <summary>
    /// Gets the message frequency, if there is no such frequency, returns the standard channel frequency.
    /// </summary>
    public int GetFrequency(EntityUid source, RadioChannelPrototype channel)
    {
        if (TryComp<RadioMicrophoneComponent>(source, out var radioMicrophone))
            return radioMicrophone.Frequency;

        // Frontier: add support for handheld radio frequency
        if (TryComp<HandheldRadioComponent>(source, out var radio))
            return radio.Frequency;
        // End Frontier

        return channel.Frequency;
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (!TryComp(uid, out ActorComponent? actor))
            return;

        // Palmtree/Coyote: ghosts hear radio without degradation or static.
        if (HasComp<GhostHearingComponent>(uid))
        {
            _netMan.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
            return;
        }

        var chatMess = MangleRadioMessage(
            uid,
            ref args,
            out var degradationParams);

        if (degradationParams.DropMessageEntirely || degradationParams.DropMessage)
            return;

        // Play the static for the receiver.
        var staticEv = new DoRadioStaticEvent(
            uid,
            args.MessageSource,
            actor.PlayerSession.AttachedEntity,
            args.Channel.ID,
            args.Message,
            degradationParams);
        RaiseLocalEvent(uid, ref staticEv);

        _netMan.ServerSendMessage(chatMess, actor.PlayerSession.Channel);
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(EntityUid messageSource, string message, ProtoId<RadioChannelPrototype> channel, EntityUid radioSource, int? frequency = null, bool escapeMarkup = true) // Frontier: added frequency
    {
        SendRadioMessage(messageSource, message, _prototype.Index(channel), radioSource, frequency: frequency, escapeMarkup: escapeMarkup); // Frontier: added frequency
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    /// <param name="messageSource">Entity that spoke the message</param>
    /// <param name="radioSource">Entity that picked up the message and will send it, e.g. headset</param>
    public void SendRadioMessage(EntityUid messageSource, string message, RadioChannelPrototype channel, EntityUid radioSource, int? frequency = null, bool escapeMarkup = true) // Nuclear-14: add frequency
    {
        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        var evt = new TransformSpeakerNameEvent(messageSource, MetaData(messageSource).EntityName);
        RaiseLocalEvent(messageSource, evt);

        // Frontier: add name transform event
        var transformEv = new RadioTransformMessageEvent(channel, radioSource, evt.VoiceName, message, messageSource);
        RaiseLocalEvent(radioSource, ref transformEv);
        message = transformEv.Message;
        messageSource = transformEv.MessageSource;
        // End Frontier

        var name = transformEv.Name; // Frontier: evt.VoiceName<transformEv.Name
        name = FormattedMessage.EscapeText(name);

        SpeechVerbPrototype speech;
        if (evt.SpeechVerb != null && _prototype.TryIndex(evt.SpeechVerb, out var evntProto))
            speech = evntProto;
        else
            speech = _chat.GetSpeechVerb(messageSource, message);

        var content = escapeMarkup
            ? FormattedMessage.EscapeText(message)
            : message;

        // Frontier: append frequency if the channel requests it
        string channelText;
        if (channel.ShowFrequency)
            channelText = $"\\[{channel.LocalizedName} ({frequency})\\]";
        else
            channelText = $"\\[{channel.LocalizedName}\\]";
        // End Frontier

        var verb = Loc.GetString(_random.Pick(speech.SpeechVerbStrings)); // Palmtree/Coyote: reused by degradation rewraps

        var wrappedMessage = Loc.GetString(speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            ("color", channel.Color),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("verb", verb),
            ("channel", channelText), // Frontier: $"\\[{channel.LocalizedName}\\]"<channelText
            ("name", name),
            ("message", content));

        // most radios are relayed to chat, so lets parse the chat message beforehand
        var chat = new ChatMessage(
            ChatChannel.Radio,
            message,
            wrappedMessage,
            NetEntity.Invalid,
            null);
        var chatMsg = new MsgChatMessage { Message = chat };

        // Palmtree/Coyote: keep the pieces so Shortband range degradation can rebuild the message.
        var messageData = new RadioMessageDataHolder(
            speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            channel.Color,
            speech.FontId,
            speech.FontSize,
            verb,
            channelText,
            name,
            content,
            messageSource,
            channel);

        var ev = new RadioReceiveEvent(message, messageSource, channel, radioSource, chatMsg, messageData);

        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMapId = Transform(radioSource).MapID;
        var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
        var sourceServerExempt = _exemptQuery.HasComp(radioSource);

        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();

        if (frequency == null) // Nuclear-14
            frequency = GetFrequency(messageSource, channel); // Nuclear-14

        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!HasComp<GhostComponent>(receiver) && GetFrequency(receiver, channel) != frequency) // Nuclear-14
                continue; // Nuclear-14

            if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                continue;

            // Palmtree/Coyote: hard range cutoff for range-limited channels.
            if (channel.MaxRange is { } maxRange && maxRange > 0)
            {
                var sourcePos = Transform(radioSource).WorldPosition;
                var targetPos = transform.WorldPosition;
                if ((sourcePos - targetPos).Length() > maxRange)
                    continue;
            }

            // don't need telecom server for long range channels or handheld radios and intercoms
            var needServer = !channel.LongRange && !sourceServerExempt;
            if (needServer && !hasActiveServer)
                continue;

            // check if message can be sent to specific receiver
            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            // send the message
            RaiseLocalEvent(receiver, ref ev);
        }

        if (name != Name(messageSource))
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
        else
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

        _replay.RecordServerMessage(chat);
        _messages.Remove(message);
    }

    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }

    // Palmtree/Coyote: Shortband range degradation starts here.

    /// <summary>
    /// Mangles a radio message based on the receiving channel's range properties.
    /// </summary>
    public MsgChatMessage MangleRadioMessage(
        EntityUid radioReceiver,
        ref RadioReceiveEvent args,
        out RadioDegradationParams degradationParams)
    {
        degradationParams = new RadioDegradationParams(
            wordDropPercentage: 0,
            letterDropPercentage: 0,
            fontSizeDecrease: 0,
            generifyChannel: false,
            generifyStatic: false,
            generifyName: false,
            dropMessage: false,
            dropMessageEntirely: false);

        var channel = args.Channel;
        if (!channel.UseRangeDegradation || args.MessageDataHolder is null)
            return args.ChatMsg;

        var senderGridUid = _t.GetGrid(args.MessageSource);
        var receiverGridUid = _t.GetGrid(radioReceiver);

        // Same grid? They can hear you clearly.
        if (senderGridUid.HasValue
            && receiverGridUid.HasValue
            && senderGridUid == receiverGridUid)
            return args.ChatMsg;

        var receiverPos = _t.GetWorldPosition(radioReceiver);
        var senderPos = _t.GetWorldPosition(args.MessageSource);
        var distance = (receiverPos - senderPos).Length();

        var optiRange = channel.OptimalRange;

        // Early bounce if within optimal range.
        if (distance <= optiRange)
            return args.ChatMsg;

        // Get the current grid's diameter if either entity is on a grid.
        var gridDiameter = 0f;
        if (TryComp(receiverGridUid, out MapGridComponent? receiverGrid))
            gridDiameter += Math.Max(receiverGrid.LocalAABB.Width, receiverGrid.LocalAABB.Height);
        if (TryComp(senderGridUid, out MapGridComponent? senderGrid))
            gridDiameter += Math.Max(senderGrid.LocalAABB.Width, senderGrid.LocalAABB.Height);

        optiRange += Math.Min(gridDiameter * 0.5f, optiRange * 0.2f); // max 20% increase from grids
        if (distance <= optiRange)
            return args.ChatMsg;

        var radioData = args.MessageDataHolder;

        var lightDegradDist = optiRange + channel.LightDegradationRange;
        var heavyDegradDist = lightDegradDist + channel.HeavyDegradationRange;

        // Calculate the total range where degradation occurs.
        var degradationRange = heavyDegradDist - optiRange;
        var proportionThrough = (distance - optiRange) / degradationRange;
        proportionThrough = Math.Clamp(proportionThrough, 0f, 1f);

        var charDropBase = Math.Max(channel.DegradationMaxPercent * proportionThrough, channel.DegradationMinPercent);
        var wordDropBase = charDropBase * channel.DegradationWordMult;

        // Beyond the heavy degradation range, there is a chance to not hear anything at all.
        if (distance > heavyDegradDist)
        {
            var missChance = channel.BeyondHeavyDegradationDropChance;
            if (_random.Prob(missChance) || distance > heavyDegradDist + channel.TotalDegradationRange)
            {
                degradationParams.DropMessage = true;
                degradationParams.GenerifyStatic = true;
                return args.ChatMsg;
            }

            // Otherwise, degrade heavily.
            var maxDeg = channel.DegradationMaxPercent * channel.HeavyDegradationMultiplier;
            degradationParams.WordDropPercentage = maxDeg;
            degradationParams.LetterDropPercentage = maxDeg;
            degradationParams.FontSizeDecrease = 4;
            degradationParams.GenerifyChannel = true;
            degradationParams.GenerifyStatic = true;
            degradationParams.GenerifyName = true;
            degradationParams.Whisperfy = true;
        }
        // Less than heavy degradation distance but more than light degradation distance.
        else if (distance > lightDegradDist)
        {
            degradationParams.WordDropPercentage = wordDropBase * channel.HeavyDegradationMultiplier;
            degradationParams.LetterDropPercentage = charDropBase * channel.HeavyDegradationMultiplier;
            degradationParams.FontSizeDecrease = 3;
            degradationParams.GenerifyName = true;
            degradationParams.GenerifyChannel = true;
            degradationParams.Whisperfy = true;
        }
        else
        {
            degradationParams.WordDropPercentage = wordDropBase;
            degradationParams.LetterDropPercentage = charDropBase;
            degradationParams.FontSizeDecrease = 2;
        }

        var mangledMessage = MangleMessage(
            radioData.Message,
            degradationParams.WordDropPercentage,
            degradationParams.LetterDropPercentage);

        var newFontSize = Math.Max(1, radioData.FontSize - degradationParams.FontSizeDecrease);

        var name = radioData.Name;
        if (degradationParams.GenerifyName)
        {
            name = GenerifyName(radioData);
            degradationParams.NameOverride = name;
        }

        var channelText = radioData.ChannelText;
        var channelColor = radioData.Color;
        if (degradationParams.GenerifyChannel)
        {
            channelText = "\\[???\\]";
            channelColor = Color.Gray;
            degradationParams.ColorOverride = channelColor;
        }

        var wrappedMessage = Loc.GetString(
            radioData.LocBase,
            ("color", channelColor),
            ("fontType", radioData.FontType),
            ("fontSize", newFontSize),
            ("verb", radioData.Verb),
            ("channel", channelText),
            ("name", name),
            ("message", mangledMessage));

        var chat = new ChatMessage(
            ChatChannel.Radio,
            mangledMessage,
            wrappedMessage,
            NetEntity.Invalid,
            null);

        return new MsgChatMessage { Message = chat };
    }

    /// <summary>
    /// Mangles a message by randomly dropping words and letters based on the given percentages.
    /// Dropped letters are replaced with static characters.
    /// </summary>
    private string MangleMessage(string message, float wordDropFloat, float letterDropFloat)
    {
        List<string> staticReplacements = new()
        {
            "▓", "▒", "░",
        };

        wordDropFloat = Math.Clamp(wordDropFloat, 0f, 1f);
        letterDropFloat = Math.Clamp(letterDropFloat, 0f, 1f);
        var words = message.Split(' ');

        // Use the percentages to determine actual counts to drop instead of rolling each word,
        // so a 10% drop rate on 45 words drops 4 or 5 words guaranteed.
        var wordsToDrop = (int) Math.Round(words.Length * wordDropFloat);
        wordsToDrop = Math.Clamp(wordsToDrop, 1, words.Length);

        var wordIndices = Enumerable
            .Range(0, words.Length)
            .OrderBy(x => _random.Next())
            .Take(wordsToDrop)
            .ToHashSet();

        var letterDropWordsCount = (int) Math.Round((words.Length - wordIndices.Count) * letterDropFloat);
        var letterDropWords = Enumerable
            .Range(0, words.Length)
            .Where(x => !wordIndices.Contains(x))
            .OrderBy(x => _random.Next())
            .Take(letterDropWordsCount)
            .ToHashSet();

        for (var i = 0; i < words.Length; i++)
        {
            if (wordIndices.Contains(i))
            {
                var newWord = "";
                for (var j = 0; j < words[i].Length; j++)
                    newWord += _random.Pick(staticReplacements);
                words[i] = newWord;
                continue;
            }

            if (letterDropWords.Contains(i))
            {
                var letters = words[i].ToCharArray();
                var lettersToDrop = (int) Math.Round(letters.Length * letterDropFloat);
                lettersToDrop = Math.Clamp(lettersToDrop, 1, letters.Length);
                var letterIndices = Enumerable
                    .Range(0, letters.Length)
                    .OrderBy(x => _random.Next())
                    .Take(lettersToDrop)
                    .ToHashSet();

                for (var j = 0; j < letters.Length; j++)
                {
                    if (letterIndices.Contains(j))
                        letters[j] = _random.Pick(staticReplacements)[0];
                }

                words[i] = new string(letters);
            }
            else // Just replace one letter in the word with static.
            {
                var letters = words[i].ToCharArray();
                var letterIdx = _random.Next(0, letters.Length);
                letters[letterIdx] = _random.Pick(staticReplacements)[0];
                words[i] = new string(letters);
            }
        }

        if (wordDropFloat + letterDropFloat >= 0.5f)
        {
            for (var k = 0; k < words.Length / 4; k++)
            {
                var idx1 = _random.Next(0, words.Length);
                var idx2 = _random.Next(0, words.Length);
                (words[idx1], words[idx2]) = (words[idx2], words[idx1]);
            }
        }

        var finalMessage = "";
        for (var i = 0; i < words.Length; i++)
        {
            finalMessage += words[i];
            if (i < words.Length - 1)
            {
                if (_random.Prob(letterDropFloat))
                    finalMessage += _random.Pick(staticReplacements);
                else
                    finalMessage += " ";
            }
        }

        return finalMessage;
    }

    /// <summary>
    /// Replaces the name with their generic equivalent, as set by Identity.
    /// </summary>
    private string GenerifyName(RadioMessageDataHolder radioData)
    {
        var source = radioData.Sender;
        if (source is null)
            return "someone";

        var age = 18;
        var gender = Gender.Epicene;
        var species = SharedHumanoidAppearanceSystem.DefaultSpecies;

        // Always use their actual age and gender, since that can't really be changed by an ID.
        if (TryComp<HumanoidAppearanceComponent>(source.Value, out var appearance))
        {
            gender = appearance.Gender;
            age = appearance.Age;
            species = appearance.Species;
        }

        var middleaged = _ham.GetAgeRepresentation(species, age);
        var genderString = gender switch
        {
            Gender.Female => Loc.GetString("identity-gender-feminine"),
            Gender.Male => Loc.GetString("identity-gender-masculine"),
            Gender.Epicene or Gender.Neuter or _ => Loc.GetString("identity-gender-person")
        };

        return $"{middleaged} {genderString}";
    }
}
