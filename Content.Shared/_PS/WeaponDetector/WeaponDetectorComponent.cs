using Content.Shared.Access;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.WeaponDetector;

/// <summary>
/// Walk-through weapon detector (SS13 scanner gate, guns mode).
/// Beeps when an entity carrying a gun steps onto its tile.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WeaponDetectorComponent : Component
{
    [DataField]
    public WeaponDetectorScanMode ScanMode = WeaponDetectorScanMode.All;

    /// <summary>
    /// Guns carrying this tag are skipped in <see cref="WeaponDetectorScanMode.LethalOnly"/> mode.
    /// </summary>
    [DataField]
    public ProtoId<TagPrototype> ToyTag = "ToySidearm";

    /// <summary>
    /// ID cards carrying this access bypass <see cref="WeaponDetectorScanMode.SecurityBypass"/> detectors,
    /// same as carrying a mindshield (SS13 behavior).
    /// </summary>
    [DataField]
    public ProtoId<AccessLevelPrototype> BypassAccess = "Armory";

    [DataField]
    public SoundSpecifier AlarmSound = new SoundPathSpecifier("/Audio/_PS/Machines/weapon_detector_alarm.ogg");

    [DataField]
    public LocId AlarmPopup = "weapon-detector-alarm";

    [DataField]
    public LocId BypassPopup = "weapon-detector-bypassed";

    /// <summary>
    /// Minimum time between alarm beeps (SS13 uses 2 seconds).
    /// </summary>
    [DataField]
    public TimeSpan BeepCooldown = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the scanline overlay stays on Scanning/Alarm before returning to its idle state.
    /// </summary>
    [DataField]
    public TimeSpan VisualDuration = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether the detector refuses to scan unless anchored.
    /// </summary>
    [DataField]
    public bool RequiresAnchored = true;

    /// <summary>
    /// Whether the detector refuses to scan without power.
    /// </summary>
    [DataField]
    public bool RequiresPower = true;

    [ViewVariables]
    public TimeSpan NextBeep;

    [ViewVariables]
    public TimeSpan ResetAt;

    [ViewVariables, AutoNetworkedField]
    public WeaponDetectorState VisualState = WeaponDetectorState.Off;
}

[Serializable, NetSerializable]
public enum WeaponDetectorScanMode : byte
{
    /// <summary>Beep for any gun, including toys.</summary>
    All,
    /// <summary>Beep for guns, except ones carrying <see cref="WeaponDetectorComponent.ToyTag"/>.</summary>
    LethalOnly,
    /// <summary>
    /// Beep for any gun, unless the carrier has a mindshield or
    /// <see cref="WeaponDetectorComponent.BypassAccess"/> on their ID.
    /// </summary>
    SecurityBypass,
}

[Serializable, NetSerializable]
public enum WeaponDetectorVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum WeaponDetectorState : byte
{
    Off,
    Passive,
    Scanning,
    Alarm,
}
