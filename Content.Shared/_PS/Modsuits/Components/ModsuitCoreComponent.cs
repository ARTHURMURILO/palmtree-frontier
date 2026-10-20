using Robust.Shared.GameStates;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// A swappable MODsuit power core. The standard core stores charge directly; variants can override this with
/// a different source (the Protean core uses the refactory organ's metal store).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModsuitCoreComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Charge = 100f;

    [DataField, AutoNetworkedField]
    public float MaxCharge = 100f;

    [DataField, AutoNetworkedField]
    public bool Infinite;

    /// <summary>
    /// Multiplier applied to the theme's passive drain while the suit is active.
    /// </summary>
    [DataField]
    public float DrainMultiplier = 1f;

    public float GetCharge() => Infinite ? float.PositiveInfinity : Charge;

    public float GetMaxCharge() => Infinite ? float.PositiveInfinity : MaxCharge;

    public bool HasCharge(float amount) => Infinite || Charge >= amount;

    public void AddCharge(float amount)
    {
        if (!Infinite)
            Charge = Math.Min(MaxCharge, Charge + amount);
    }

    public bool TryUseCharge(float amount)
    {
        if (Infinite)
            return true;

        if (Charge < amount)
            return false;

        Charge -= amount;
        return true;
    }
}
