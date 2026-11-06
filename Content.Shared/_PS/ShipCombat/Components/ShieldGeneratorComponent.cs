namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: projects a ship's directional shields. Must be powered by the ship's bluespace power
/// sink (<see cref="BluespacePoweredComponent"/> on the Shields pool).
/// </summary>
[RegisterComponent]
public sealed partial class ShieldGeneratorComponent : Component
{
    /// <summary>Shield capacity per facing.</summary>
    [DataField]
    public float MaxPerFacing = 300f;

    /// <summary>Shield restored to every facing per six-second pulse at 100% power.</summary>
    [DataField]
    public float RegenPerPulse = 20f;
}
