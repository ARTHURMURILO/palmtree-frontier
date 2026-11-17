using Content.Shared._PS.Interactions;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._PS.Interactions;

/// <summary>
/// Client side of the interaction panel BUI. The panel is bound to the acting entity;
/// the target is carried in the state.
/// </summary>
[UsedImplicitly]
public sealed class InteractionBoundUserInterface : BoundUserInterface
{
    private InteractionWindow? _window;

    public InteractionBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<InteractionWindow>();

        _window.OnInteractionSelected += id => SendMessage(new InteractionSelectedMessage(id));
        _window.OnAutoToggled += id => SendMessage(new InteractionToggleAutoMessage(id));
        _window.OnFavoriteToggled += id => SendMessage(new InteractionToggleFavoriteMessage(id));
        _window.OnPaceChanged += pace => SendMessage(new InteractionSetPaceMessage(pace));
        _window.OnConsentChanged += enabled => SendMessage(new InteractionSetConsentMessage(enabled));
        _window.OnSoundsChanged += enabled => SendMessage(new InteractionSetSoundsMessage(enabled));
        _window.OnArousalMultiplierChanged += (use, value) =>
            SendMessage(new InteractionSetArousalMultiplierMessage(use, value));
        _window.OnMoaningMultiplierChanged += (use, value) =>
            SendMessage(new InteractionSetMoaningMultiplierMessage(use, value));
        _window.OnGenitalAroused += (type, aroused) =>
            SendMessage(new InteractionGenitalArouseMessage(type, aroused));
        _window.OnGenitalVisibilityChanged += (type, visibility) =>
            SendMessage(new InteractionGenitalSetVisibilityMessage(type, visibility));
        _window.OnLayerModeChanged += mode => SendMessage(new InteractionSetLayerModeMessage(mode));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is InteractionBoundUserInterfaceState interactionState)
            _window?.UpdateState(interactionState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _window?.Dispose();
        _window = null;
    }
}
