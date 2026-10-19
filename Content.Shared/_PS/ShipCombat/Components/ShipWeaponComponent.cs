using Robust.Shared.Audio;
using Robust.Shared.Maths;

namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a ship weapon controlled by the tactical console (a turret or a stationary weapon).
/// Weapons are grouped by the device-link sink port they are linked on (Primary/Secondary/Aux);
/// they are powered remotely by the bluespace power sink.
/// </summary>
[RegisterComponent]
public sealed partial class ShipWeaponComponent : Component
{
    /// <summary>How the weapon is aimed.</summary>
    [DataField("weaponType")]
    public ShipWeaponType Type = ShipWeaponType.Turret;

    /// <summary>Half of the turret's firing arc, in degrees, around the angle it was placed at.</summary>
    [DataField]
    public float ArcHalfAngle = 22.5f;

    /// <summary>Turret rotation speed, in degrees per second.</summary>
    [DataField]
    public float RotationSpeed = 60f;

    /// <summary>How closely a turret must face its target before it fires, in degrees.</summary>
    [DataField]
    public float FireTolerance = 5f;

    /// <summary>When true, the weapon fires through its own grid instead of colliding with it.</summary>
    [DataField]
    public bool IgnoreOwnGrid = true;

    /// <summary>
    /// Effective shoot-through state: <see cref="IgnoreOwnGrid"/> combined with the
    /// <c>ps.ship_combat.turrets_shoot_through_hull</c> cvar. Set every tick by
    /// <c>ShipWeaponSystem</c>; while it is false a turret holds fire when its own hull blocks the
    /// firing line, and the core projectile/hitscan hooks leave collisions alone.
    /// </summary>
    public bool ShootThroughHull;

    /// <summary>
    /// When true, the weapon can fire while the ship is on MASTER SAFE (future traction beams).
    /// </summary>
    [DataField]
    public bool FiresWhileSafe;

    /// <summary>Half of the angle a fixed weapon will fire within, in degrees.</summary>
    [DataField]
    public float FixedFireAngle = 45f;

    /// <summary>
    /// Played when the weapon fires. Ship combat is larger scale than the PVS bubble, so the sound is
    /// broadcast to everyone inside the ship combat range and stays audible across it; the prototypes
    /// leave <c>Gun.soundGunshot</c> unset so nothing is played twice.
    /// </summary>
    [DataField]
    public SoundSpecifier? GunshotSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/laser.ogg");

    /// <summary>The angle the weapon was placed at; its arc is centred on this (runtime).</summary>
    public Angle BaseAngle;

    /// <summary>Idle sweep direction (runtime).</summary>
    public bool IdleClockwise = true;
}
