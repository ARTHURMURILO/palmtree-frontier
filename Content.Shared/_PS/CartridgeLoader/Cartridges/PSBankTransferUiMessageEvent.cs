using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class PSBankTransferUiMessageEvent : CartridgeMessageEvent
{
    public readonly int TargetAccount;
    public readonly int Amount;

    public PSBankTransferUiMessageEvent(int targetAccount, int amount)
    {
        TargetAccount = targetAccount;
        Amount = amount;
    }
}
