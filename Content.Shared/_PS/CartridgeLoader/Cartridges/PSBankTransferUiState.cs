using Robust.Shared.Serialization;

namespace Content.Shared._PS.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class PSBankTransferUiState : BoundUserInterfaceState
{
    public readonly int AccountNumber;
    public readonly int Balance;
    public readonly string Result;
    public readonly bool Success;

    public PSBankTransferUiState(int accountNumber, int balance, string result, bool success)
    {
        AccountNumber = accountNumber;
        Balance = balance;
        Result = result;
        Success = success;
    }
}
