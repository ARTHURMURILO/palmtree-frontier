namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a ship's directional shields. Split into four facings; a facing with charge absorbs
/// hits from its direction until it collapses.
/// </summary>
/// <remarks>
/// Absorption follows STO: a facing with any charge absorbs the whole hit (and can drop from one
/// big volley); a facing at 0 goes down and only re-arms once it regenerates above
/// <see cref="ReArmFraction"/> of its maximum, which keeps the collapse alarm from flapping.
/// </remarks>
[RegisterComponent]
public sealed partial class ShipShieldComponent : Component
{
    /// <summary>Current shield charge per facing, indexed by <see cref="ShieldFacing"/>.</summary>
    [DataField]
    public float[] Facings = new float[4];

    /// <summary>True while a facing is up (absorbing). A collapsed facing re-arms above the threshold.</summary>
    public bool[] Up = { true, true, true, true };

    /// <summary>Fraction of its maximum a collapsed facing must regenerate to before it re-arms.</summary>
    [DataField]
    public float ReArmFraction = 0.2f;

    /// <summary>When the next six-second regeneration pulse is due.</summary>
    public TimeSpan NextPulse;

    /// <summary>When the facings may be redistributed again.</summary>
    public TimeSpan NextRedistribution;

    /// <summary>True once the first working generator has filled the facings.</summary>
    public bool Initialized;
}
