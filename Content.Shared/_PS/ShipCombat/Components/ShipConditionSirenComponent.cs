using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipCombat.Components;

/// <summary>
/// Palmtree: a ship condition siren. While the grid's condition is <see cref="ActiveCondition"/>
/// (RED by default) it shows a strong rotating red light and plays a looping alarm through its
/// <c>AmbientSound</c>; otherwise it is dark and silent. Unpowered sirens stay off.
/// </summary>
[RegisterComponent]
public sealed partial class ShipConditionSirenComponent : Component
{
    /// <summary>The condition that turns the siren on.</summary>
    [DataField]
    public ShipCondition ActiveCondition = ShipCondition.Red;
}

/// <summary>Sprite layers of the condition siren (emergency light sprite).</summary>
[Serializable, NetSerializable]
public enum ShipConditionSirenLayers : byte
{
    Base,
    Off,
    On,
}

/// <summary>Appearance keys of the condition siren.</summary>
[Serializable, NetSerializable]
public enum ShipConditionSirenVisuals : byte
{
    Active,
}
