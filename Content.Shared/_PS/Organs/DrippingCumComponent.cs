using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.Organs;

/// <summary>
/// Semen left inside a mob. <see cref="DrippingCumSystem"/> drains it into floor puddles while the
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

    /// <summary>Server-only: total units already dripped, used to pick the decal stage.</summary>
    public double Dripped;

    /// <summary>Server-only: next drip tick.</summary>
    public TimeSpan NextDrip;

    /// <summary>
    /// Server-only: set when the mob moved or performed an interaction since the last drip. A drip
    /// consumes it, so an idle mob never drips.
    /// </summary>
    public bool Ready;

    /// <summary>Server-only: grid-local position at the last drip check, used to notice movement.</summary>
    public Vector2 LastPosition;
}
