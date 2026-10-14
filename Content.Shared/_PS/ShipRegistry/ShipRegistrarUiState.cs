using Robust.Shared.Serialization;

namespace Content.Shared._PS.ShipRegistry;

/// <summary>
/// State for the Ship Registrar window.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipRegistrarUiState : BoundUserInterfaceState
{
    /// <summary>Whether the privileged (Station Representative) ID card is inserted.</summary>
    public bool HasPrivilegedId;

    /// <summary>Name on the privileged ID card.</summary>
    public string? PrivilegedIdName;

    /// <summary>Whether the target (ship owner) ID card is inserted.</summary>
    public bool HasTargetId;

    /// <summary>Name on the target ID card.</summary>
    public string? TargetIdName;

    /// <summary>Bank balance of the player using the console.</summary>
    public int Balance;

    /// <summary>Registration fee.</summary>
    public int Fee;

    /// <summary>Docked grids at this station that are not registered yet.</summary>
    public List<ShipRegistrarGridUi> Grids = new();

    public ShipRegistrarUiState(
        bool hasPrivilegedId,
        string? privilegedIdName,
        bool hasTargetId,
        string? targetIdName,
        int balance,
        int fee,
        List<ShipRegistrarGridUi> grids)
    {
        HasPrivilegedId = hasPrivilegedId;
        PrivilegedIdName = privilegedIdName;
        HasTargetId = hasTargetId;
        TargetIdName = targetIdName;
        Balance = balance;
        Fee = fee;
        Grids = grids;
    }
}

/// <summary>
/// One unregistered grid docked at the station.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipRegistrarGridUi
{
    public NetEntity Grid;
    public string Name = string.Empty;
    public string Size = string.Empty;
    public int Value;

    public ShipRegistrarGridUi(NetEntity grid, string name, string size, int value)
    {
        Grid = grid;
        Name = name;
        Size = size;
        Value = value;
    }
}
