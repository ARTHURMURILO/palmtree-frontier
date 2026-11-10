using Content.Shared._PS.Atmos.Components;

namespace Content.Client._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: client side of the heat pump. Only refreshes the open window when the server sends new
/// state.
/// </summary>
public sealed class GasHeatPumpSystem : Content.Shared._PS.Atmos.EntitySystems.SharedGasHeatPumpSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasHeatPumpComponent, AfterAutoHandleStateEvent>(OnStateHandled);
    }

    private void OnStateHandled(Entity<GasHeatPumpComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateUi(ent);
    }

    protected override void UpdateUi(Entity<GasHeatPumpComponent> ent)
    {
        if (UserInterfaceSystem.TryGetOpenUi(ent.Owner, ent.Comp.UiKey, out var bui))
            bui.Update();
    }
}
