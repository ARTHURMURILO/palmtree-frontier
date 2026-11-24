using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._PS.Weapons.Melee;

/// <summary>
/// Marks a melee weapon as a whip. Whips can lash outside combat mode, but only deal damage
/// (including stamina damage) while the user is in combat mode.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WhipComponent : Component
{
    /// <summary>
    /// Stamina damage applied on a successful lash while in combat mode.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaDamage = 20f;

    /// <summary>
    /// Stamina damage applied on a lash made outside combat mode. These lashes are harmless
    /// otherwise, but stamina damage can still knock targets down.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OutOfCombatStaminaDamage = 10f;

    /// <summary>
    /// Sprite used for the rope drawn from the wielder to the strike point.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier RopeSprite = new SpriteSpecifier.Rsi(
        new ResPath("_PS/Objects/Weapons/Melee/whip_rope.rsi"), "rope");
}
