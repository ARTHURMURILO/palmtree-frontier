using Robust.Shared.Serialization;

namespace Content.Shared._PS.BluespaceDock;

/// <summary>
/// State for the Bluespace Dock console window.
/// </summary>
[Serializable, NetSerializable]
public sealed class BluespaceDockUiState : BoundUserInterfaceState
{
    /// <summary>Whether an ID card is in the console's slot.</summary>
    public bool HasIdCard;

    /// <summary>Name on the inserted ID card (the dock's "character").</summary>
    public string? IdName;

    /// <summary>Bank balance of the player using the console.</summary>
    public int Balance;

    /// <summary>Whether the inserted ID's deed points at a live ship.</summary>
    public bool HasActiveShip;

    /// <summary>Full name of the active ship.</summary>
    public string? ActiveShipName;

    /// <summary>Appraised value of the active ship.</summary>
    public int ActiveShipValue;

    /// <summary>Tax owed to store the active ship.</summary>
    public int StoreCost;

    /// <summary>Whether the active ship can be stored right now (client-side hint; re-checked on the server).</summary>
    public bool CanStore;

    /// <summary>Localization key explaining why the active ship cannot be stored, if any.</summary>
    public string? StoreBlockedReason;

    /// <summary>Whether the active ship can be sold like at a shipyard.</summary>
    public bool CanSellActive;

    /// <summary>Ships this character has stored in the dock.</summary>
    public List<BluespaceDockEntryUi> StoredShips = new();

    public BluespaceDockUiState(
        bool hasIdCard,
        string? idName,
        int balance,
        bool hasActiveShip,
        string? activeShipName,
        int activeShipValue,
        int storeCost,
        bool canStore,
        string? storeBlockedReason,
        bool canSellActive,
        List<BluespaceDockEntryUi> storedShips)
    {
        HasIdCard = hasIdCard;
        IdName = idName;
        Balance = balance;
        HasActiveShip = hasActiveShip;
        ActiveShipName = activeShipName;
        ActiveShipValue = activeShipValue;
        StoreCost = storeCost;
        CanStore = canStore;
        StoreBlockedReason = storeBlockedReason;
        CanSellActive = canSellActive;
        StoredShips = storedShips;
    }
}

/// <summary>
/// One row in the stored ships list.
/// </summary>
[Serializable, NetSerializable]
public sealed class BluespaceDockEntryUi
{
    public string ShipId = string.Empty;
    public string Name = string.Empty;
    public string Model = string.Empty;
    public int Value;
    public int SellValue;
    public bool CanRetrieve;
    public bool CanSell;
    public string? BlockedReason;

    public BluespaceDockEntryUi(string shipId, string name, string model, int value, int sellValue,
        bool canRetrieve, bool canSell, string? blockedReason)
    {
        ShipId = shipId;
        Name = name;
        Model = model;
        Value = value;
        SellValue = sellValue;
        CanRetrieve = canRetrieve;
        CanSell = canSell;
        BlockedReason = blockedReason;
    }
}
