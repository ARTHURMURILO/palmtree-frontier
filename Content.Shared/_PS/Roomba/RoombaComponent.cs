using System.Numerics;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Shared._PS.Roomba;

/// <summary>
/// Palmtree: the roomba's brain. <see cref="Content.Server._PS.Roomba.RoombaSystem"/> drives the bot
/// like a real robot vacuum - constant motion, bumping into walls and turning away - while it scrubs
/// cleanable decals and mops puddles underfoot, and occasionally talks to itself.
/// </summary>
/// <remarks>
/// The component is shared (not just server-side) because the mob-collision system runs on both
/// sides: clients predict their own pushes, so the "roomba never shoves mobs" rule has to be known
/// client-side too (<see cref="SharedRoombaSystem"/>). The HTN task for <c>MobRoomba</c> only keeps
/// the NPC awake; the component holds the configuration and runtime state, while
/// <see cref="Content.Server._PS.Roomba.RoombaSystem"/> runs the movement and cleaning logic.
/// </remarks>
[RegisterComponent]
public sealed partial class RoombaComponent : Component
{
    /// <summary>The container holding the roomba's replaceable internal trash bag.</summary>
    public const string TrashBagContainerId = "roomba_trash_bag";

    /// <summary>
    /// Whether the roomba is switched on. Roombas are built and mapped switched off and have to be
    /// clicked (or activated) to start cleaning. While off they do not drive, clean, or speak.
    /// </summary>
    [DataField]
    public bool Enabled;

    /// <summary>
    /// Whether the roomba vacuums up loose trash. Toggled from the right-click menu; floor scrubbing
    /// and puddle mopping keep working when this is off.
    /// </summary>
    [DataField]
    public bool CollectTrash = true;

    /// <summary>Whether the roomba's voice module is muted (it stops saying lines entirely).</summary>
    [DataField]
    public bool MuteSpeech;

    /// <summary>How short a leg may be, in tiles.</summary>
    [DataField]
    public float MinLegDistance = 1f;

    /// <summary>How long a leg may be, in tiles.</summary>
    [DataField]
    public float MaxLegDistance = 3.5f;

    /// <summary>Random heading drift, in degrees, applied when a new leg starts.</summary>
    [DataField]
    public float LegJitter = 18f;

    /// <summary>Minimum turn, in degrees, when the roomba bumps into something.</summary>
    [DataField]
    public float BumpTurnMin = 60f;

    /// <summary>Maximum turn, in degrees, when the roomba bumps into something.</summary>
    [DataField]
    public float BumpTurnMax = 140f;

    /// <summary>How far the roomba must move to count as progress on a leg.</summary>
    [DataField]
    public float BumpDistance = 0.12f;

    /// <summary>Seconds without progress before the roomba considers itself bumped.</summary>
    [DataField]
    public float BumpTime = 0.7f;

    /// <summary>Tile sampling step for the "never drive off the grid" line check.</summary>
    [DataField]
    public float LineCheckStep = 0.35f;

    /// <summary>
    /// Extra floor, in tiles, required beyond a leg's target. The roomba carries momentum, so a leg
    /// that ends right at the edge of the floor could still overshoot into space.
    /// </summary>
    [DataField]
    public float EdgeMargin = 0.6f;

    /// <summary>How close obstacles have to be to a leg's line to make the roomba pick another way.</summary>
    [DataField]
    public float ObstacleRadius = 0.35f;

    /// <summary>How far the roomba moves between scrub passes.</summary>
    [DataField]
    public float ScrubDistance = 0.45f;

    /// <summary>Radius, in tiles, of the decal scrub pass around the roomba.</summary>
    [DataField]
    public float ScrubRadius = 0.75f;

    /// <summary>Radius, in tiles, of the trash pickup pass around the roomba.</summary>
    [DataField]
    public float TrashPickupRadius = 0.75f;

    /// <summary>Maximum number of trash items picked up in a single pass.</summary>
    [DataField]
    public int TrashPickupLimit = 10;

    /// <summary>Sound played when the roomba vacuums up trash.</summary>
    [DataField]
    public SoundSpecifier TrashPickupSound = new SoundCollectionSpecifier("trashBagRustle");

    /// <summary>Water drained from the absorbent solution per decal scrubbed.</summary>
    [DataField]
    public FixedPoint2 WaterPerDecal = FixedPoint2.New(0.5f);

    /// <summary>Sound played while scrubbing.</summary>
    [DataField]
    public SoundSpecifier ScrubSound = new SoundPathSpecifier("/Audio/Effects/Fluids/vacuum-cleaner-fast.ogg",
        AudioParams.Default.WithVariation(0.25f));

    /// <summary>Sound played when the roomba is switched on or off.</summary>
    [DataField]
    public SoundSpecifier PowerSound = new SoundPathSpecifier("/Audio/Machines/button.ogg",
        AudioParams.Default.WithVolume(-2f));

    /// <summary>Minimum time between scrub sounds, so a very dirty floor does not machine-gun them.</summary>
    [DataField]
    public float ScrubSoundCooldown = 1.2f;

    /// <summary>Chance to say something when the roomba bumps into a wall.</summary>
    [DataField]
    public float BumpSpeechChance = 0.25f;

    /// <summary>Minimum time between bump complaints, so a wedged roomba does not chatter.</summary>
    [DataField]
    public float BumpSpeechCooldown = 6f;

    /// <summary>Lower bound of the random interval between idle quotes, in seconds.</summary>
    [DataField]
    public float IdleSpeechMinSeconds = 90f;

    /// <summary>Upper bound of the random interval between idle quotes, in seconds.</summary>
    [DataField]
    public float IdleSpeechMaxSeconds = 240f;

    /// <summary>Seconds between checks for nearby characters that are not wearing anything.</summary>
    [DataField]
    public float NudeCheckInterval = 1f;

    /// <summary>How far the roomba can notice a nude character.</summary>
    [DataField]
    public float NudeCheckRange = 6f;

    /// <summary>Chance per check to comment on a nude character.</summary>
    [DataField]
    public float NudeSpeechChance = 0.35f;

    /// <summary>Cooldown after a nude comment, in seconds.</summary>
    [DataField]
    public float NudeSpeechCooldown = 25f;

    // Runtime state. Not data fields: this is per-round server state (unused by the client).

    /// <summary>Current heading (unit vector). Zero until the first leg.</summary>
    public Vector2 Heading = Vector2.Zero;

    /// <summary>Coordinates of the current leg's destination.</summary>
    public EntityCoordinates Target;

    /// <summary>Is the roomba currently driving a leg?</summary>
    public bool LegActive;

    /// <summary>Position the last progress check was based on.</summary>
    public Vector2 ProgressPosition;

    /// <summary>When progress was last made.</summary>
    public TimeSpan ProgressTime;

    /// <summary>Don't retry a new leg before this time.</summary>
    public TimeSpan NextLegRetry;

    /// <summary>Position of the last scrub pass.</summary>
    public Vector2 LastScrubPosition;

    /// <summary>Last position known to be over a real tile.</summary>
    public EntityCoordinates LastSafeCoordinates;

    /// <summary>When the scrub sound may play again.</summary>
    public TimeSpan NextScrubSound;

    /// <summary>Bump complaints are ignored before this time.</summary>
    public TimeSpan NextBumpSpeech;

    /// <summary>When the roomba wants to say something idle next.</summary>
    public TimeSpan NextIdleSpeech;

    /// <summary>When to check for nearby nude characters next.</summary>
    public TimeSpan NextNudeCheck;

    /// <summary>Nude comments are ignored before this time.</summary>
    public TimeSpan NextNudeSpeech;
}
