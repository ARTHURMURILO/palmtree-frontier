using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a torpedo launcher. It is fired manually from the tactical console's FIRE TORPEDOES
/// button (never automatically), needs a set lock, exposed space in front of it and power. Torpedoes
/// are unlimited; each launcher has its own cooldown.
/// </summary>
[RegisterComponent]
public sealed partial class ShipTorpedoLauncherComponent : Component
{
    /// <summary>The torpedo prototype this launcher fires.</summary>
    [DataField]
    public EntProtoId Torpedo = "ShipTorpedo";

    /// <summary>Minimum time between two launches from this launcher.</summary>
    [DataField]
    public TimeSpan FireCooldown = TimeSpan.FromSeconds(6);

    /// <summary>Played when a torpedo launches.</summary>
    [DataField]
    public SoundSpecifier? LaunchSound = new SoundPathSpecifier("/Audio/_PS/ShipCombat/tng_torpedo_clean.ogg");

    /// <summary>When this launcher may fire again (runtime).</summary>
    public TimeSpan NextFire;
}
