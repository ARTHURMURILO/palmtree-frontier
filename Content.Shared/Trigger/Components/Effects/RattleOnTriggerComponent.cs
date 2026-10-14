using Content.Shared.Mobs;
using Content.Shared.Radio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Trigger.Components.Effects;

/// <summary>
/// Sends an emergency message over coms when triggered giving information about the entity's mob status.
/// If TargetUser is true then the user's mob state will be used instead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RattleOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    /// The radio channel the message will be sent to.
    /// </summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> RadioChannel = "Syndicate";

    /// <summary>
    /// The message to be send depending on the target's current mob state.
    /// </summary>
    [DataField]
    public Dictionary<MobState, LocId> Messages = new()
    {
        {MobState.Critical, "rattle-on-trigger-critical-message"},
        {MobState.Dead, "rattle-on-trigger-dead-message"}
    };

    // Palmtree: messages used when the target is on an expedition map, where coordinates are of no
    // use to anyone listening.
    [DataField]
    public Dictionary<MobState, LocId> MessagesOnExpedition = new()
    {
        {MobState.Critical, "rattle-on-trigger-critical-message-expedition"},
        {MobState.Dead, "rattle-on-trigger-dead-message-expedition"}
    };

    // Palmtree: messages used when the rattle is re-broadcast (a recovered body arriving back on
    // the main map).
    [DataField]
    public Dictionary<MobState, LocId> RetryMessages = new()
    {
        {MobState.Critical, "rattle-on-trigger-critical-message-repeat"},
        {MobState.Dead, "rattle-on-trigger-dead-message-repeat"}
    };
    // End Palmtree
}
