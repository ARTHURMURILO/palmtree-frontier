using Content.Shared.Access;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// Palmtree: Ship Registrar console. Turns a docked, unregistered grid (a hand-built ship) into a
/// proper civilian vessel: station, deed, shuttle record, IFF name and a
/// <see cref="RegisteredVesselComponent"/> marker. Requires the Station Representative's ID and the
/// ship owner's ID, like the ID card console.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
[Access(typeof(SharedShipRegistrarSystem), Other = AccessPermissions.ReadWriteExecute)]
public sealed partial class ShipRegistrarConsoleComponent : Component
{
    public static string PrivilegedIdCardSlotId = "ShipRegistrar-privilegedId";

    /// <summary>Maximum length of a registered ship's name.</summary>
    public const int MaxNameLength = 32;

    /// <summary>Maximum length of a registered ship's registration suffix.</summary>
    public const int MaxSuffixLength = 12;

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    /// <summary>
    /// Access levels added to the owner's ID when their ship is registered, exactly like a shipyard
    /// console grants on a purchase.
    /// </summary>
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> NewAccessLevels = new() { "Captain" };

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
