namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a torpedo. A projectile that slowly steers towards the grid it was launched at; the
/// limited turn rate means a manoeuvring target can make it miss.
/// </summary>
[RegisterComponent]
public sealed partial class ShipTorpedoComponent : Component
{
    /// <summary>Cruise speed, in tiles per second.</summary>
    [DataField]
    public float Speed = 20f;

    /// <summary>Maximum turn rate, in degrees per second.</summary>
    [DataField]
    public float TurnRate = 20f;

    /// <summary>Steering stays off for this long after launch (photon torpedoes).</summary>
    [DataField]
    public TimeSpan SteeringDelay = TimeSpan.Zero;

    /// <summary>Steering ramps from zero to full over this time after the delay.</summary>
    [DataField]
    public TimeSpan SteeringRamp = TimeSpan.Zero;

    /// <summary>Distance at which the incoming-torpedo alarm starts, in tiles.</summary>
    [DataField]
    public float AlarmRange = 200f;

    /// <summary>Heading error above this many degrees counts as a miss (alarm stays silent).</summary>
    [DataField]
    public float InboundDeviation = 30f;

    /// <summary>The grid this torpedo was launched at (runtime).</summary>
    public EntityUid? Target;

    /// <summary>The grid that launched it (runtime).</summary>
    public EntityUid? Source;

    /// <summary>When it was launched (runtime).</summary>
    public TimeSpan LaunchTime;
}
