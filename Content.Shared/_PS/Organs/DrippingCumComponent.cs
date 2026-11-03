using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.Organs;

/// <summary>
/// Semen left inside a mob. <see cref="DrippingCumSystem"/> drains it into floor droplets while the
/// mob is bottomless (no jumpsuit), pausing while clothed.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DrippingCumComponent : Component
{
    /// <summary>Maximum amount of semen a mob can hold. Anything past this is lost.</summary>
    public const double MaxCapacity = 1000;

    /// <summary>Semen units still stored inside the mob.</summary>
    [DataField, AutoNetworkedField]
    public double Amount;

    /// <summary>Server-only: total number of drips already placed, used to pick the decal stage.</summary>
    public double Dripped;

    /// <summary>Server-only: next drip tick.</summary>
    public TimeSpan NextDrip;

    /// <summary>
    /// Server-only: starts false when semen is first stored. Set once the mob moves or performs an
    /// interaction; the latch is never consumed by a drip, so the mob keeps dripping while it stands
    /// still until the reservoir is empty.
    /// </summary>
    public bool Ready;

    /// <summary>Server-only: grid-local position at the last drip check, used to notice movement.</summary>
    public Vector2 LastPosition;
}
