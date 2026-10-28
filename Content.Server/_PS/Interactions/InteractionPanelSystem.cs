using System.Linq;
using Content.Server._PS.Organs;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Preferences.Managers; // Palmtree: NSFW age gate
using Content.Shared._PS.Consent; // Palmtree: NSFW age gate
using Content.Shared._PS.Interactions;
using Content.Shared._PS.Organs;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._PS.Interactions;

/// <summary>
/// Server-authoritative port of Sandstorm's interaction panel: data-driven social/lewd
/// interactions on another character, with session consent, genital marking requirements,
/// lust/moaning/climax and auto-repeat.
/// </summary>
public sealed partial class InteractionPanelSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly GenitalSystem _genitals = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly GenitalOrganSystem _organs = default!;
    [Dependency] private readonly DrippingCumSystem _drip = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!; // Palmtree: interaction audit log
    [Dependency] private readonly IServerPreferencesManager _preferences = default!; // Palmtree: NSFW age gate

    /// <summary>Verb category shown when right-clicking a character.</summary>
    public static readonly VerbCategory InteractCategory =
        new("verb-categories-interact", "/Textures/Interface/VerbIcons/love.png");

    /// <summary>Panel speed options in seconds, matching Sandstorm's GLOB.interaction_speeds.</summary>
    public static readonly float[] Speeds = { 4f, 2f, 1f, 0.8f, 0.5f };

    /// <summary>Genitals consulted by <see cref="InteractionRequirements.AnyGenitalExposed"/>.</summary>
    private static readonly GenitalType[] ExposureGenitalTypes =
    {
        GenitalType.Penis,
        GenitalType.Balls,
        GenitalType.Vagina,
        GenitalType.Breasts,
        GenitalType.Anus,
    };

    private const float InteractionRange = 6f;
    private const float SoundRange = 4f;

    /// <summary>
    /// Range of lewd interaction emotes. Unlike the old PVS multiplier this is an absolute
    /// world-space range and is blocked by walls (see <see cref="SendErpEmote"/>).
    /// </summary>
    private const float ErpEmoteRange = 4f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InteractionPanelComponent, GetVerbsEvent<Verb>>(OnGetVerbs);

        // Bound user interface messages. The panel is bound to the actor so each player has
        // their own state (this engine version has one UI state per entity, not per user).
        SubscribeLocalEvent<InteractionPanelComponent, InteractionSelectedMessage>(OnSelected);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionToggleAutoMessage>(OnToggleAuto);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionSetPaceMessage>(OnSetPace);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionToggleFavoriteMessage>(OnToggleFavorite);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionGenitalArouseMessage>(OnGenitalArouse);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionGenitalSetVisibilityMessage>(OnGenitalSetVisibility);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionSetConsentMessage>(OnSetConsent);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionSetSoundsMessage>(OnSetSounds);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionSetArousalMultiplierMessage>(OnSetArousalMultiplier);
        SubscribeLocalEvent<InteractionPanelComponent, InteractionSetMoaningMultiplierMessage>(OnSetMoaningMultiplier);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.InteractWithEntity,
                new PointerInputCmdHandler(OnInteractWithEntity))
            .Register<InteractionPanelSystem>();

        ValidatePrototypes();
    }

    /// <summary>
    /// Content sanity check: unknown flag/requirement names used to be silently dropped, which
    /// turned a content typo into a missing gate. Message pools must not be empty either, since the
    /// panel picks from them at runtime.
    /// </summary>
    private void ValidatePrototypes()
    {
        foreach (var proto in _prototype.EnumeratePrototypes<InteractionPrototype>())
        {
            foreach (var name in proto.Flags)
            {
                if (!Enum.TryParse<InteractionFlags>(name, ignoreCase: true, out _))
                    Log.Error($"Interaction '{proto.ID}' has unknown flag '{name}'; it is ignored at runtime.");
            }

            ValidateRequirementNames(proto.ID, proto.UserRequirements);
            ValidateRequirementNames(proto.ID, proto.TargetRequirements);

            if (proto.Messages.Count == 0)
                Log.Error($"Interaction '{proto.ID}' has no messages; it cannot be performed.");
        }
    }

    private void ValidateRequirementNames(string id, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!Enum.TryParse<InteractionRequirements>(name, ignoreCase: true, out _))
                Log.Error($"Interaction '{id}' has unknown requirement '{name}'; it is ignored at runtime.");
        }
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<InteractionPanelSystem>();
        base.Shutdown();
    }

    #region Opening

    private void OnGetVerbs(EntityUid uid, InteractionPanelComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!HasComp<InteractionPanelComponent>(args.User))
            return;

        var target = args.Target;
        var user = args.User;

        // Palmtree: no panel for players who have not enabled NSFW content.
        if (!IsNsfwAllowed(user))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("interaction-verb-open"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/love.png")),
            Category = InteractCategory,
            Act = () => OpenPanel(user, target),
        });
    }

    private bool OnInteractWithEntity(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is not { } user || !user.IsValid())
            return false;

        if (!HasComp<InteractionPanelComponent>(user))
            return false;

        // Clicking empty space opens the self panel (masturbation and other UserIsTarget actions).
        if (!uid.IsValid() || !HasComp<InteractionPanelComponent>(uid))
        {
            OpenPanel(user, user);
            return true;
        }

        OpenPanel(user, uid);
        return true;
    }

    /// <summary>Opens (or retargets) the interaction panel for <paramref name="user"/>.</summary>
    public void OpenPanel(EntityUid user, EntityUid target)
    {
        if (!HasComp<InteractionPanelComponent>(user) || !HasComp<InteractionPanelComponent>(target))
            return;

        // Palmtree: the panel is unavailable until the player answers the NSFW consent prompt.
        // There is deliberately no partial panel for non-consenting players.
        if (!IsNsfwAllowed(user))
        {
            _popup.PopupEntity(Loc.GetString("interaction-nsfw-disabled"), user, user, PopupType.Medium);
            return;
        }

        if (user != target && !_interaction.InRangeUnobstructed(user, target, InteractionRange))
        {
            _popup.PopupEntity(Loc.GetString("interaction-target-too-far"), target, user);
            return;
        }

        var state = EnsureState(user);
        state.Target = target;
        StopAutoRepeat(state);

        UpdatePanelUi(user, state);
        _ui.OpenUi(user, InteractionUiKey.Key, user);
    }

    #endregion

    #region Requirement checks

    /// <summary>
    /// Returns whether <paramref name="proto"/> can currently be performed by <paramref name="user"/>
    /// on <paramref name="target"/>. When <paramref name="popup"/> is set, failures notify the user.
    /// </summary>
    public bool CanPerform(InteractionPrototype proto, EntityUid user, EntityUid target, bool popup)
    {
        var flags = GetFlags(proto);

        // A mid-climax actor cannot start another climax.
        if (proto.ForceClimax && EnsureState(user).ClimaxPulsesLeft > 0)
            return false;

        // Sandstorm self/other semantics: self-only interactions need USER_IS_TARGET,
        // interactions without it cannot target yourself. SelfOrOther bypasses both rules.
        var selfOrOther = flags.HasFlag(InteractionFlags.SelfOrOther);

        if (user == target && !selfOrOther && !flags.HasFlag(InteractionFlags.UserIsTarget))
            return false;

        if (user != target && !selfOrOther && flags.HasFlag(InteractionFlags.UserIsTarget))
            return false;

        if (flags.HasFlag(InteractionFlags.Adjacent) &&
            !_interaction.InRangeUnobstructed(user, target, proto.MaxDistance))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("interaction-target-too-far"), target, user);
            return false;
        }

        if (proto.InteractionType == InteractionType.Lewd)
        {
            // Palmtree: non-sexual lewd acts (kissing) skip the account-level NSFW age gate;
            // the voluntary per-session toggle still applies to everyone.
            var accountGate = !proto.NonSexual;

            if (!HasConsent(user, accountGate))
            {
                if (popup)
                    _popup.PopupEntity(Loc.GetString("interaction-consent-self-disabled"), user, user);
                return false;
            }

            if (!HasConsent(target, accountGate))
            {
                if (popup)
                    _popup.PopupEntity(Loc.GetString("interaction-consent-target-disabled"), target, user);
                return false;
            }
        }

        if (!CheckRequirements(GetRequirements(proto.UserRequirements), user, user, popup, false))
            return false;

        if (!CheckRequirements(GetRequirements(proto.TargetRequirements), target, user, popup, true))
            return false;

        return true;
    }

    /// <summary>
    /// Session consent (a missing state means the default, enabled). When
    /// <paramref name="accountGate"/> is set, the account-level NSFW age gate is required too.
    /// Entities without a player session (NPCs, test mobs) keep the legacy behavior.
    /// </summary>
    public bool HasConsent(EntityUid uid, bool accountGate = true)
    {
        if (TryComp<InteractionStateComponent>(uid, out var state) && !state.Consent)
            return false;

        return !accountGate || IsNsfwAllowed(uid);
    }

    /// <summary>Palmtree: whether the entity's player has enabled NSFW content (age gate).</summary>
    public bool IsNsfwAllowed(EntityUid uid)
    {
        // Mobs without a session (NPCs, test mobs) keep the legacy behavior.
        if (!_player.TryGetSessionByEntity(uid, out var session))
            return true;

        return IsNsfwAllowed(session);
    }

    /// <summary>
    /// Palmtree: whether the session's account may see NSFW content. Fail-closed: unknown or
    /// not-yet-loaded preferences count as not consenting.
    /// </summary>
    public bool IsNsfwAllowed(ICommonSession session)
    {
        return _preferences.TryGetCachedPreferences(session.UserId, out var prefs)
               && prefs.NsfwConsent == NsfwConsentState.Allowed;
    }

    private bool CheckRequirements(InteractionRequirements requirements, EntityUid subject, EntityUid actor, bool popup, bool isTarget)
    {
        if (requirements == InteractionRequirements.None)
            return true;

        if (requirements.HasFlag(InteractionRequirements.Mouth))
        {
            if (!HasComp<HumanoidAppearanceComponent>(subject) || _genitals.IsMouthCovered(subject))
                return FailRequirement(actor, subject, popup, isTarget, "mouth");
        }

        if (requirements.HasFlag(InteractionRequirements.Hands) && !HasComp<HandsComponent>(subject))
            return FailRequirement(actor, subject, popup, isTarget, "hands");

        // Palmtree: "Clean yourself" only appears when there is actually stored semen to clean up.
        if (requirements.HasFlag(InteractionRequirements.HasStoredCum) &&
            (!TryComp<DrippingCumComponent>(subject, out var dripComp) || dripComp.Amount <= 0))
        {
            return FailRequirement(actor, subject, popup, isTarget, "cum");
        }

        // Palmtree: any one exposed genital is enough (used by the manual Climax action so it is
        // unavailable while fully clothed, matching the other lewd interactions).
        if (requirements.HasFlag(InteractionRequirements.AnyGenitalExposed))
        {
            var anyExposed = false;
            foreach (var type in ExposureGenitalTypes)
            {
                if (_genitals.GetExposure(subject, type) == GenitalExposure.Exposed)
                {
                    anyExposed = true;
                    break;
                }
            }

            if (!anyExposed)
                return FailRequirement(actor, subject, popup, isTarget, "exposed");
        }

        if (!CheckGenitalRequirement(requirements, subject, actor, popup, isTarget, GenitalType.Penis))
            return false;

        if (!CheckGenitalRequirement(requirements, subject, actor, popup, isTarget, GenitalType.Balls))
            return false;

        if (!CheckGenitalRequirement(requirements, subject, actor, popup, isTarget, GenitalType.Vagina))
            return false;

        if (!CheckGenitalRequirement(requirements, subject, actor, popup, isTarget, GenitalType.Breasts))
            return false;

        if (!CheckGenitalRequirement(requirements, subject, actor, popup, isTarget, GenitalType.Anus))
            return false;

        return true;
    }

    private bool CheckGenitalRequirement(
        InteractionRequirements requirements,
        EntityUid subject,
        EntityUid actor,
        bool popup,
        bool isTarget,
        GenitalType type)
    {
        var (exposedFlag, unexposedFlag, key) = type switch
        {
            GenitalType.Penis => (InteractionRequirements.PenisExposed, InteractionRequirements.PenisUnexposed, "penis"),
            GenitalType.Balls => (InteractionRequirements.BallsExposed, InteractionRequirements.BallsUnexposed, "balls"),
            GenitalType.Vagina => (InteractionRequirements.VaginaExposed, InteractionRequirements.VaginaUnexposed, "vagina"),
            GenitalType.Breasts => (InteractionRequirements.BreastsExposed, InteractionRequirements.BreastsUnexposed, "breasts"),
            GenitalType.Anus => (InteractionRequirements.AnusExposed, InteractionRequirements.AnusUnexposed, "anus"),
            _ => (InteractionRequirements.None, InteractionRequirements.None, "genital"),
        };

        var requiresExposed = requirements.HasFlag(exposedFlag);
        var requiresUnexposed = requirements.HasFlag(unexposedFlag);
        if (!requiresExposed && !requiresUnexposed)
            return true;

        var exposure = _genitals.GetExposure(subject, type);

        // Both flags set means "any state, but must have the genital" (Sandstorm behavior).
        if (requiresExposed && requiresUnexposed)
        {
            if (exposure == GenitalExposure.None)
                return FailRequirement(actor, subject, popup, isTarget, $"no-{key}");

            return true;
        }

        if (requiresExposed)
        {
            if (exposure == GenitalExposure.None)
                return FailRequirement(actor, subject, popup, isTarget, $"no-{key}");

            if (exposure != GenitalExposure.Exposed)
                return FailRequirement(actor, subject, popup, isTarget, $"{key}-exposed");
        }

        if (requiresUnexposed)
        {
            if (exposure == GenitalExposure.None)
                return FailRequirement(actor, subject, popup, isTarget, $"no-{key}");

            if (exposure != GenitalExposure.Unexposed)
                return FailRequirement(actor, subject, popup, isTarget, $"{key}-unexposed");
        }

        return true;
    }

    private bool FailRequirement(EntityUid actor, EntityUid subject, bool popup, bool isTarget, string key)
    {
        if (popup)
        {
            // Popups are shown to the acting player; "target" variants explain the other party.
            _popup.PopupEntity(
                Loc.GetString(isTarget ? $"interaction-require-target-{key}" : $"interaction-require-user-{key}"),
                subject,
                actor,
                PopupType.Medium);
        }

        return false;
    }

    #endregion

    #region Execution

    /// <summary>
    /// Performs an interaction if it is currently valid. Used by the panel and by auto-repeat.
    /// </summary>
    public bool TryPerform(EntityUid user, EntityUid target, InteractionPrototype proto, bool applyCooldown = true)
    {
        var state = EnsureState(user);

        // Fail-safe: a mid-climax actor cannot start another climax even if the UI is stale.
        if (proto.ForceClimax && state.ClimaxPulsesLeft > 0)
            return false;

        // Manual actions (applyCooldown) surface why they failed; auto-repeat stays quiet.
        if (!CanPerform(proto, user, target, popup: applyCooldown))
            return false;

        var now = _timing.CurTime;
        if (applyCooldown && now < state.LastInteractionTime + proto.Cooldown)
            return false;

        if (applyCooldown)
            state.LastInteractionTime = now;

        // Palmtree: "Clean yourself" wipes the stored semen so the drip stops. Clear before the
        // activity notification so the removed reservoir cannot be reactivated.
        if (proto.ClearsStoredCum)
            _drip.ClearSemen(user);

        // Palmtree: moving or acting is what makes stored cum start to drip (both participants are
        // active during an interaction, including the receiving one).
        _drip.NotifyActivity(user);
        if (target != user)
            _drip.NotifyActivity(target);

        // Manual climax (SPLURT's Climax verb) and "Cum on them". The generic Climax action has no
        // cum target of its own and reuses the last interaction's target when the actor climaxes
        // during it, so a manual climax inside a partner still creampies and drips.
        if (proto.ForceClimax)
        {
            var effectiveProto = proto;
            if (proto.CumTarget == null &&
                state.LastInteractionId is { } lastId &&
                state.LastInteractionTarget == target &&
                now <= state.LastInteractionRepeat + proto.ContinueTimeout &&
                _prototype.TryIndex<InteractionPrototype>(lastId, out var lastProto) &&
                lastProto.CumTarget != null)
            {
                effectiveProto = lastProto;
            }

            TriggerClimax(user, state, target, effectiveProto, isActor: true);
            LogInteraction(user, target, effectiveProto); // Palmtree: admin log

            if (IsPanelOpen(user, state))
                UpdatePanelUi(user, state);

            if (TryComp<InteractionStateComponent>(target, out var climaxTargetState) && IsPanelOpen(target, climaxTargetState))
                UpdatePanelUi(target, climaxTargetState);

            return true;
        }

        // Palmtree: do not turn the participants automatically; they keep their own facing.
        var targetName = Identity.Name(target, EntityManager);
        var continuing = state.LastInteractionId == proto.ID &&
                         state.LastInteractionTarget == target &&
                         now <= state.LastInteractionRepeat + proto.ContinueTimeout;

        var pool = continuing && proto.ContinueMessages is { Count: > 0 }
            ? proto.ContinueMessages
            : proto.Messages;

        if (pool.Count == 0)
        {
            // ValidatePrototypes logs this at startup; guard so a content mistake cannot throw here.
            Log.Error($"Interaction '{proto.ID}' has no messages and cannot be performed.");
            return false;
        }

        var message = Loc.GetString(_random.Pick(pool), ("target", targetName));

        state.LastInteractionId = proto.ID;
        state.LastInteractionTarget = target;
        state.LastInteractionRepeat = now;

        if (proto.InteractionType == InteractionType.Lewd)
            SendErpEmote(user, message, proto.NonSexual);
        else
            _chat.TrySendInGameICMessage(user, message, InGameICChatType.Emote,
                ChatTransmitRange.Normal, hideLog: false, ignoreActionBlocker: true);

        PlayInteractionSound(user, proto.Sound, proto.Volume, proto.InteractionType == InteractionType.Lewd, proto.NonSexual);

        // Palmtree: cleanup actions (Clean yourself) do not arouse anyone or add lust.
        if (!proto.ClearsStoredCum)
            ApplyInteractionArousal(user, target, proto);

        // Palmtree: log the start of an interaction (auto-repeat continuations are not logged again).
        if (!continuing)
            LogInteraction(user, target, proto);

        if (IsPanelOpen(user, state))
            UpdatePanelUi(user, state);

        if (TryComp<InteractionStateComponent>(target, out var targetState) && IsPanelOpen(target, targetState))
            UpdatePanelUi(target, targetState);

        return true;
    }

    /// <summary>
    /// Palmtree: admin-log a performed interaction with both participants, so admins can audit the
    /// interaction panel. Auto-repeat continuations are not logged again by the caller.
    /// </summary>
    private void LogInteraction(EntityUid user, EntityUid target, InteractionPrototype proto)
    {
        if (user == target)
        {
            _adminLogger.Add(LogType.Interaction, LogImpact.Medium,
                $"{ToPrettyString(user):player} performed interaction {proto.ID} ({Loc.GetString(proto.Name)}) on themselves");
        }
        else
        {
            _adminLogger.Add(LogType.Interaction, LogImpact.Medium,
                $"{ToPrettyString(user):player} performed interaction {proto.ID} ({Loc.GetString(proto.Name)}) on {ToPrettyString(target):target}");
        }
    }

    /// <summary>Applies lust/moans/climax to both participants of a lewd interaction.</summary>
    private void ApplyInteractionArousal(EntityUid user, EntityUid target, InteractionPrototype proto)
    {
        if (proto.InteractionType != InteractionType.Lewd || !HasComp<HumanoidAppearanceComponent>(user))
            return;

        var userState = EnsureState(user);

        // Mid-climax participants do not gain lust (fail-safe: no auto-climax re-trigger while pulsing).
        var userClimaxing = userState.ClimaxPulsesLeft > 0;

        // Involved organs become aroused.
        _organs.SetAroused(user, GenitalType.Penis, true);
        _organs.SetAroused(user, GenitalType.Vagina, true);

        if (user == target)
        {
            if (!userClimaxing)
            {
                if (proto.MinLust > 0)
                {
                    if (GetLust(user, userState) < proto.MinLust)
                        SetLust(user, userState, proto.MinLust);
                }
                else if (proto.Lust > 0)
                {
                    AddLust(user, userState, proto.Lust, userState.UseArousalMultiplier, userState.ArousalMultiplier);
                }
            }

            TryMoan(user, userState);
            TryClimax(user, userState, null, proto, isActor: true);
            return;
        }

        if (HasComp<HumanoidAppearanceComponent>(target))
        {
            _organs.SetAroused(target, GenitalType.Penis, true);
            _organs.SetAroused(target, GenitalType.Vagina, true);
        }

        var targetState = HasComp<HumanoidAppearanceComponent>(target) ? EnsureState(target) : null;
        var targetClimaxing = targetState is { ClimaxPulsesLeft: > 0 };

        if (!userClimaxing)
        {
            if (proto.MinLust > 0)
            {
                if (GetLust(user, userState) < proto.MinLust)
                    SetLust(user, userState, proto.MinLust);
            }
            else if (proto.Lust > 0)
            {
                AddLust(user, userState, proto.Lust, userState.UseArousalMultiplier, userState.ArousalMultiplier);
            }
        }

        if (targetState != null && !targetClimaxing)
        {
            if (proto.MinLust > 0)
            {
                if (GetLust(target, targetState) < proto.MinLust)
                    SetLust(target, targetState, proto.MinLust);
            }
            else if (proto.Lust > 0)
            {
                AddLust(target, targetState, proto.Lust,
                    targetState.UseArousalMultiplier, targetState.ArousalMultiplier);
            }
        }

        TryMoan(user, userState);
        TryClimax(user, userState, target, proto, isActor: true);

        if (targetState != null)
        {
            TryMoan(target, targetState);

            // Receiver-side acts (riding, taking it) reuse the interaction's cum target for the
            // target's climax, so the penetrator's semen lands in the actor when they finish.
            TryClimax(target, targetState, user, proto, isActor: true);
        }
    }

    #endregion

    #region Sounds

    /// <summary>
    /// Plays an interaction sound. Lewd sounds are only sent to nearby players who did not opt
    /// out of lewd sounds (mobs without a session hear nothing, mirroring Sandstorm); normal
    /// interaction sounds use regular PVS.
    /// </summary>
    private void PlayInteractionSound(EntityUid source, SoundSpecifier? sound, float volume, bool lewd, bool nonSexual = false)
    {
        if (sound == null)
            return;

        var audioParams = AudioParams.Default.WithVolume(volume).WithVariation(0.1f);

        if (!lewd)
        {
            _audio.PlayPvs(sound, source, audioParams);
            return;
        }

        var recipients = new List<ICommonSession>();
        var sourcePos = Transform(source).MapPosition;

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } listener)
                continue;

            if (TryComp<InteractionStateComponent>(listener, out var listenerState) && !listenerState.LewdSounds)
                continue;

            // Palmtree: NSFW age gate. Non-consenting players never receive sexual sounds;
            // non-sexual entries (kissing) are exempt from the gate, but the per-listener
            // LewdSounds preference above still applies to them.
            if (!nonSexual && !IsNsfwAllowed(session))
                continue;

            // Ghosts do not get to snoop on ERP sounds.
            if (HasComp<GhostComponent>(listener))
                continue;

            var listenerPos = Transform(listener).MapPosition;
            if (listenerPos.MapId != sourcePos.MapId)
                continue;

            if ((listenerPos.Position - sourcePos.Position).Length() > SoundRange)
                continue;

            recipients.Add(session);
        }

        if (recipients.Count == 0)
            return;

        _audio.PlayEntity(sound, Filter.Empty().AddPlayers(recipients), source, false, audioParams);
    }

    #endregion

    #region Auto repeat

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<InteractionStateComponent>();

        while (query.MoveNext(out var uid, out var state))
        {
            // Palmtree: an admin may revoke NSFW consent while the panel is open or auto-repeat
            // is running; close the panel and stop the repeat immediately.
            if ((state.ActiveAutoInteraction != null || IsPanelOpen(uid, state)) && !IsNsfwAllowed(uid))
            {
                StopAutoRepeat(state);

                if (_ui.IsUiOpen(uid, InteractionUiKey.Key))
                    _ui.CloseUi(uid, InteractionUiKey.Key);
            }

            if (state.ActiveAutoInteraction is not { } interactionId || state.AutoTarget is not { } target)
                continue;

            if (now < state.NextAutoInteraction)
                continue;

            if (Deleted(target) || !HasComp<InteractionPanelComponent>(target) ||
                !_prototype.TryIndex<InteractionPrototype>(interactionId, out var proto))
            {
                StopAutoRepeat(state);
                continue;
            }

            if (!TryPerform(uid, target, proto, applyCooldown: false))
            {
                StopAutoRepeat(state);
                continue;
            }

            state.NextAutoInteraction = now + TimeSpan.FromSeconds(MathF.Max(state.AutoPace, 0.5f));
        }

        // Climax pulse sequences: keep emitting pulses until the stored volume or the 10-pulse
        // cap is reached. Each pulse places a decal/drip, sends the cum text and moans.
        var pulseQuery = EntityQueryEnumerator<InteractionStateComponent>();
        while (pulseQuery.MoveNext(out var uid, out var state))
        {
            if (state.ClimaxPulsesLeft <= 0 || now < state.NextClimaxPulse)
                continue;

            InteractionPrototype? proto = null;
            if (state.ClimaxPulseProto != null)
                _prototype.TryIndex<InteractionPrototype>(state.ClimaxPulseProto, out proto);

            if (EmitClimaxPulse(uid, state, proto))
                state.ClimaxPulsesLeft--;
            else
                state.ClimaxPulsesLeft = 0;

            state.NextClimaxPulse = now + state.ClimaxPulseInterval;

            if (state.ClimaxPulsesLeft <= 0)
            {
                state.ClimaxPulseRemaining = 0;
                state.ClimaxPulseTarget = null;
                state.ClimaxPulseProto = null;
                state.ClimaxPulseFirst = false;
                state.ClimaxPulseFemale = false;
            }

            if (IsPanelOpen(uid, state))
                UpdatePanelUi(uid, state);
        }
    }

    public void StopAutoRepeat(InteractionStateComponent state)
    {
        state.ActiveAutoInteraction = null;
        state.AutoTarget = null;
    }

    #endregion

    #region Helpers

    /// <summary>Builds the human-readable attribute lines shown at the top of the panel.</summary>
    private List<string> GetAttributes(EntityUid uid)
    {
        var lines = new List<string>();

        if (HasComp<HandsComponent>(uid))
            lines.Add(Loc.GetString("interaction-attr-hands"));

        if (HasComp<HumanoidAppearanceComponent>(uid))
        {
            lines.Add(Loc.GetString(_genitals.IsMouthCovered(uid)
                ? "interaction-attr-mouth-covered"
                : "interaction-attr-mouth-free"));
        }

        lines.Add(Loc.GetString(_genitals.IsBottomless(uid)
            ? "interaction-attr-bottomless"
            : "interaction-attr-clothed"));

        if (_genitals.GetExposure(uid, GenitalType.Penis) == GenitalExposure.Exposed)
            lines.Add(Loc.GetString("interaction-attr-penis"));
        if (_genitals.GetExposure(uid, GenitalType.Vagina) == GenitalExposure.Exposed)
            lines.Add(Loc.GetString("interaction-attr-vagina"));
        if (_genitals.GetExposure(uid, GenitalType.Breasts) == GenitalExposure.Exposed)
            lines.Add(Loc.GetString("interaction-attr-breasts"));
        if (_genitals.GetExposure(uid, GenitalType.Balls) == GenitalExposure.Exposed)
            lines.Add(Loc.GetString("interaction-attr-balls"));

        return lines;
    }

    private bool IsPanelOpen(EntityUid uid, InteractionStateComponent? state = null)
    {
        if (state == null && !TryComp(uid, out state))
            return false;

        return state.Target != null && _ui.IsUiOpen(uid, InteractionUiKey.Key);
    }

    private const string ErpColor = "#c060ff";

    /// <summary>
    /// Sends an ERP emote in purple. Built manually because <c>TrySendInGameICMessage</c> strips
    /// markup from emote text; the chat tags themselves are otherwise supported.
    /// </summary>
    private void SendErpEmote(EntityUid source, string message, bool nonSexual = false)
    {
        var ent = Identity.Entity(source, EntityManager);
        var name = FormattedMessage.EscapeText(Name(ent));
        var colored = $"[color={ErpColor}]{FormattedMessage.EscapeText(message)}[/color]";
        var wrapped = Loc.GetString("chat-manager-entity-me-wrap-message",
            ("entityName", name),
            ("entity", ent),
            ("message", colored));

        // Palmtree: an explicit range + line of sight instead of a PVS radius.
        // AddPlayersByPvs only checks distance (net.pvs_range * multiplier, and the
        // default 25 * 2f used to make these emotes readable 50 tiles away through walls);
        // InRangeUnOccluded raycasts for occluders, so walls block them like any interaction.
        var filter = Filter.Empty();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } attached)
                continue;

            // Ghosts do not get to snoop on ERP emotes.
            if (HasComp<GhostComponent>(attached))
                continue;

            // Palmtree: NSFW age gate. Non-consenting players never receive sexual ERP text;
            // non-sexual entries (kissing) are exempt.
            if (!nonSexual && !IsNsfwAllowed(session))
                continue;

            if (!_examine.InRangeUnOccluded(source, attached, ErpEmoteRange))
                continue;

            filter.AddPlayer(session);
        }

        _chatManager.ChatMessageToManyFiltered(filter, ChatChannel.Emotes, message, wrapped, source, false, false, null);
    }

    /// <summary>Parses the prototype's flag name list into a bitmask.</summary>
    public static InteractionFlags GetFlags(InteractionPrototype proto)
    {
        var result = InteractionFlags.None;

        foreach (var name in proto.Flags)
        {
            if (Enum.TryParse<InteractionFlags>(name, ignoreCase: true, out var flag))
                result |= flag;
        }

        return result;
    }

    /// <summary>Parses a requirement name list into a bitmask.</summary>
    public static InteractionRequirements GetRequirements(IEnumerable<string> names)
    {
        var result = InteractionRequirements.None;

        foreach (var name in names)
        {
            if (Enum.TryParse<InteractionRequirements>(name, ignoreCase: true, out var requirement))
                result |= requirement;
        }

        return result;
    }

    #endregion
}
