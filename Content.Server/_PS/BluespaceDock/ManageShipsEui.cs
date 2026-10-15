using Content.Server._PS.ShipRegistry;
using Content.Server.EUI;
using Content.Shared._PS.BluespaceDock;
using Content.Shared.Eui;

namespace Content.Server._PS.BluespaceDock;

/// <summary>
/// Palmtree: admin tool for player ships in the world. Lists ships with deeds (not players, so ships
/// whose owner is offline are included), previews one with the live ship camera and can delete one,
/// clearing any deed that pointed at it.
/// </summary>
public sealed partial class ManageShipsEui : BaseEui
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private readonly BluespaceDockSystem _dock = default!;

    private string? _status;
    private bool _statusIsError;

    public ManageShipsEui()
    {
        IoCManager.InjectDependencies(this);
        _dock = _entityManager.System<BluespaceDockSystem>();
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case ManageShipsPreviewMessage preview:
                if (_entityManager.TryGetEntity(preview.Grid, out var grid))
                    _eui.OpenEui(new ShipPreviewEui(grid.Value), Player);
                break;
            case ManageShipsDeleteMessage delete:
                Delete(delete.Grid);
                break;
        }
    }

    public override EuiStateBase GetNewState()
    {
        var ships = new List<ManageShipEntry>();
        foreach (var ship in _dock.GetDeedShips())
        {
            ships.Add(new ManageShipEntry(_entityManager.GetNetEntity(ship.Grid), ship.Name, ship.Owner, ship.Docked));
        }

        return new ManageShipsEuiState(ships, _status, _statusIsError);
    }

    private void Delete(NetEntity netGrid)
    {
        var name = string.Empty;
        foreach (var ship in _dock.GetDeedShips())
        {
            if (_entityManager.GetNetEntity(ship.Grid) == netGrid)
            {
                name = ship.Name;
                break;
            }
        }

        if (!_entityManager.TryGetEntity(netGrid, out var grid))
        {
            SetStatus(Loc.GetString("manage-ships-error-missing"), error: true);
            return;
        }

        if (!_dock.TryDeleteShip(grid.Value, out var error))
        {
            SetStatus(Loc.GetString(error ?? "manage-ships-error-missing"), error: true);
            return;
        }

        SetStatus(Loc.GetString("manage-ships-deleted", ("name", name)), error: false);
        StateDirty();
    }

    private void SetStatus(string status, bool error)
    {
        _status = status;
        _statusIsError = error;
        StateDirty();
    }
}
