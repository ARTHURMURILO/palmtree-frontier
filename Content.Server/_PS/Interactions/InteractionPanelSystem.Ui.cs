using System.Linq;
using Content.Shared._PS.Interactions;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Interactions;

public sealed partial class InteractionPanelSystem
{
    /// <summary>Rebuilds and pushes the panel state to the client.</summary>
    public void UpdatePanelUi(EntityUid user, InteractionStateComponent state)
    {
        _ui.SetUiState(user, InteractionUiKey.Key, BuildState(user, state));
    }

    /// <summary>Builds the bound UI state for the given actor.</summary>
    public InteractionBoundUserInterfaceState BuildState(EntityUid user, InteractionStateComponent state)
    {
        var result = new InteractionBoundUserInterfaceState();

        var target = state.Target;
        if (target is { } targetUid && !Deleted(targetUid))
        {
            result.Target = GetNetEntity(targetUid);
            result.TargetName = Identity.Name(targetUid, EntityManager);
            result.TargetIsSelf = targetUid == user;
            result.TargetAttributes = GetAttributes(targetUid);
        }
        else
        {
            result.Target = NetEntity.Invalid;
        }

        result.SelfAttributes = GetAttributes(user);

        result.Lust = GetLust(user, state);
        result.MaxLust = state.LustTolerance * 3;

        if (target is { } targetEntity && HasComp<HumanoidAppearanceComponent>(targetEntity))
        {
            var targetState = EnsureState(targetEntity);
            result.TargetLust = GetLust(targetEntity, targetState);
            result.TargetMaxLust = targetState.LustTolerance * 3;
        }

        foreach (var proto in _prototype.EnumeratePrototypes<InteractionPrototype>()
                     .OrderBy(p => Loc.GetString(p.Name), StringComparer.OrdinalIgnoreCase))
        {
            var available = false;
            if (target is { } targetUid2 && !Deleted(targetUid2))
                available = CanPerform(proto, user, targetUid2, false);

            result.Interactions.Add(new InteractionUiEntry
            {
                Id = proto.ID,
                Name = Loc.GetString(proto.Name),
                Type = proto.InteractionType,
                Available = available,
                Favorite = state.Favorites.Contains(proto.ID),
            });
        }

        result.Genitals = _genitals.GetGenitalEntries(user, true);

        result.Consent = state.Consent;
        result.LewdSounds = state.LewdSounds;
        result.UseArousalMultiplier = state.UseArousalMultiplier;
        result.ArousalMultiplier = state.ArousalMultiplier;
        result.UseMoaningMultiplier = state.UseMoaningMultiplier;
        result.MoaningMultiplier = state.MoaningMultiplier;
        result.LayerMode = TryComp<InteractionPanelComponent>(user, out var panel)
            ? panel.LayerMode
            : CharacterLayerMode.Default;

        result.ActiveAutoInteraction = state.ActiveAutoInteraction;
        result.AutoPace = state.AutoPace;
        result.Speeds = Speeds.ToList();

        return result;
    }

    #region Message handlers

    private void OnSelected(Entity<InteractionPanelComponent> ent, ref InteractionSelectedMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        var state = EnsureState(ent.Owner);
        if (state.Target is not { } target || Deleted(target) || !HasComp<InteractionPanelComponent>(target))
            return;

        if (!_prototype.TryIndex<InteractionPrototype>(args.InteractionId, out var proto))
            return;

        if (args.Auto)
            ToggleAutoRepeat(ent.Owner, state, target, proto);
        else
            TryPerform(ent.Owner, target, proto);

        UpdatePanelUi(ent.Owner, state);
    }

    private void OnToggleAuto(Entity<InteractionPanelComponent> ent, ref InteractionToggleAutoMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        var state = EnsureState(ent.Owner);
        if (state.Target is not { } target || Deleted(target) || !HasComp<InteractionPanelComponent>(target))
            return;

        if (!_prototype.TryIndex<InteractionPrototype>(args.InteractionId, out var proto))
            return;

        ToggleAutoRepeat(ent.Owner, state, target, proto);
        UpdatePanelUi(ent.Owner, state);
    }

    private void ToggleAutoRepeat(EntityUid user, InteractionStateComponent state, EntityUid target, InteractionPrototype proto)
    {
        if (state.ActiveAutoInteraction == proto.ID && state.AutoTarget == target)
        {
            StopAutoRepeat(state);
            return;
        }

        if (!CanPerform(proto, user, target, popup: true))
            return;

        state.ActiveAutoInteraction = proto.ID;
        state.AutoTarget = target;
        state.NextAutoInteraction = _timing.CurTime;
    }

    private void OnSetPace(Entity<InteractionPanelComponent> ent, ref InteractionSetPaceMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        var pace = args.Pace;
        if (!Speeds.Any(speed => Math.Abs(speed - pace) < 0.001f))
            return;

        var state = EnsureState(ent.Owner);
        state.AutoPace = pace;
        UpdatePanelUi(ent.Owner, state);
    }

    private void OnToggleFavorite(Entity<InteractionPanelComponent> ent, ref InteractionToggleFavoriteMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        if (!_prototype.HasIndex<InteractionPrototype>(args.InteractionId))
            return;

        var state = EnsureState(ent.Owner);
        if (!state.Favorites.Remove(args.InteractionId))
            state.Favorites.Add(args.InteractionId);

        UpdatePanelUi(ent.Owner, state);
    }

    private void OnGenitalArouse(Entity<InteractionPanelComponent> ent, ref InteractionGenitalArouseMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        if (!_organs.SetAroused(ent.Owner, args.Type, args.Aroused))
            return;

        UpdatePanelUi(ent.Owner, EnsureState(ent.Owner));
    }

    private void OnGenitalSetVisibility(Entity<InteractionPanelComponent> ent, ref InteractionGenitalSetVisibilityMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        if (!_organs.SetVisibility(ent.Owner, args.Type, args.Visibility))
            return;

        UpdatePanelUi(ent.Owner, EnsureState(ent.Owner));
    }

    private void OnSetConsent(Entity<InteractionPanelComponent> ent, ref InteractionSetConsentMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        var state = EnsureState(ent.Owner);
        state.Consent = args.Enabled;
        UpdatePanelUi(ent.Owner, state);
    }

    private void OnSetSounds(Entity<InteractionPanelComponent> ent, ref InteractionSetSoundsMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        var state = EnsureState(ent.Owner);
        state.LewdSounds = args.Enabled;
        UpdatePanelUi(ent.Owner, state);
    }

    private void OnSetArousalMultiplier(Entity<InteractionPanelComponent> ent, ref InteractionSetArousalMultiplierMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        var state = EnsureState(ent.Owner);
        state.UseArousalMultiplier = args.Use;
        state.ArousalMultiplier = Math.Clamp(args.Value, 0, 300);
        UpdatePanelUi(ent.Owner, state);
    }

    private void OnSetMoaningMultiplier(Entity<InteractionPanelComponent> ent, ref InteractionSetMoaningMultiplierMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        var state = EnsureState(ent.Owner);
        state.UseMoaningMultiplier = args.Use;
        state.MoaningMultiplier = Math.Clamp(args.Value, 0, 100);
        UpdatePanelUi(ent.Owner, state);
    }

    private void OnSetLayerMode(Entity<InteractionPanelComponent> ent, ref InteractionSetLayerModeMessage args)
    {
        if (args.Actor != ent.Owner)
            return;

        if (!Enum.IsDefined(args.Mode))
            return;

        if (ent.Comp.LayerMode == args.Mode)
            return;

        ent.Comp.LayerMode = args.Mode;
        Dirty(ent);

        UpdatePanelUi(ent.Owner, EnsureState(ent.Owner));
    }

    #endregion
}
