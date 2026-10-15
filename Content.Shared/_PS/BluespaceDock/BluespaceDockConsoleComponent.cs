using Content.Shared.Containers.ItemSlots;
using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// Bluespace Dock console: stores a player's docked civilian ship as a file in UserData and lets
/// them pull it back out (or sell it) later. Admin-spawn only, indestructible, requires power.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
[Access(typeof(SharedBluespaceDockSystem), Other = AccessPermissions.ReadWriteExecute)]
public sealed partial class BluespaceDockConsoleComponent : Component
{
    public static string TargetIdCardSlotId = "BluespaceDock-targetId";

    [DataField]
    public ItemSlot TargetIdSlot = new();

    /// <summary>
    /// Radio channel the dock announces stores, retrievals and sales on. Defaults to the same channel
    /// the shipyard console uses (the Shortband channel).
    /// </summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> AnnounceChannel = "Traffic";

    [DataField]
    public SoundSpecifier ErrorSound =
        new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier ConfirmSound =
        new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    /// <summary>
    /// The time at which the console will be able to play the deny sound again.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDenySoundTime = TimeSpan.Zero;

    /// <summary>
    /// The minimum time between playing the deny sound.
    /// </summary>
    [DataField]
    public TimeSpan DenySoundDelay = TimeSpan.FromSeconds(2);
}
