using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat;

/// <summary>
/// Palmtree: everything the shielding console window renders.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShieldingConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>The radar state (range, coordinates, angle, docks).</summary>
    public NavInterfaceState NavState { get; }

    /// <summary>The ship's condition.</summary>
    public ShipCondition Condition { get; }

    public int ShieldPower { get; }
    public int WeaponPower { get; }
    public int ThrusterPower { get; }

    public bool ShieldLocked { get; }
    public bool WeaponLocked { get; }
    public bool ThrusterLocked { get; }

    /// <summary>Current shield charge per facing, indexed by <see cref="ShieldFacing"/>.</summary>
    public float[] Facings { get; }

    /// <summary>True while each facing is up (absorbing).</summary>
    public bool[] FacingsUp { get; }

    /// <summary>Shield capacity per facing from the ship's powered generators.</summary>
    public float MaxPerFacing { get; }

    /// <summary>Seconds until the facings can be redistributed again.</summary>
    public float RedistributionCooldown { get; }

    /// <summary>`(!)` alerts: a system cannot operate.</summary>
    public bool ShieldsAlert { get; }

    public bool WeaponsAlert { get; }
    public bool ThrustersAlert { get; }

    /// <summary>True when the console is muted (no sounds).</summary>
    public bool Silent { get; }

    /// <summary>
    /// Which power pools have devices on the ship, indexed by <see cref="ShipPowerPool"/>. Absent
    /// pools are greyed out and keep their value without being normalized.
    /// </summary>
    public bool[] ActivePools { get; }

    public ShieldingConsoleBoundUserInterfaceState(
        NavInterfaceState navState,
        ShipCondition condition,
        int shieldPower,
        int weaponPower,
        int thrusterPower,
        bool shieldLocked,
        bool weaponLocked,
        bool thrusterLocked,
        float[] facings,
        bool[] facingsUp,
        float maxPerFacing,
        float redistributionCooldown,
        bool shieldsAlert,
        bool weaponsAlert,
        bool thrustersAlert,
        bool silent,
        bool[] activePools)
    {
        NavState = navState;
        Condition = condition;
        ShieldPower = shieldPower;
        WeaponPower = weaponPower;
        ThrusterPower = thrusterPower;
        ShieldLocked = shieldLocked;
        WeaponLocked = weaponLocked;
        ThrusterLocked = thrusterLocked;
        Facings = facings;
        FacingsUp = facingsUp;
        MaxPerFacing = maxPerFacing;
        RedistributionCooldown = redistributionCooldown;
        ShieldsAlert = shieldsAlert;
        WeaponsAlert = weaponsAlert;
        ThrustersAlert = thrustersAlert;
        Silent = silent;
        ActivePools = activePools;
    }
}
