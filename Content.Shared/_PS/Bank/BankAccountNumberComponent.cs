using Robust.Shared.GameStates;

namespace Content.Shared._PS.Bank;

/// <summary>
/// Palmtree: the bank account number printed on an ID card. The number is derived from the
/// player's user id and character slot, so it is stable for as long as that character slot exists.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BankAccountNumberComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Number;
}
