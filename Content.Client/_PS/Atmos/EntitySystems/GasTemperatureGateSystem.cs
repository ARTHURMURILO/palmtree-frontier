using Content.Shared._PS.Atmos.Components;

namespace Content.Client._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: client side of the temperature gate. Only refreshes the open window when the server
/// sends new state.
/// </summary>
public sealed class GasTemperatureGateSystem : Content.Shared._PS.Atmos.EntitySystems.SharedGasTemperatureGateSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasTemperatureGateComponent, AfterAutoHandleStateEvent>(OnStateHandled);
    }

    private void OnStateHandled(Entity<GasTemperatureGateComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateUi(ent);
    }

    protected override void UpdateUi(Entity<GasTemperatureGateComponent> ent)
    {
        if (UserInterfaceSystem.TryGetOpenUi(ent.Owner, ent.Comp.UiKey, out var bui))
            bui.Update();
    }
}
