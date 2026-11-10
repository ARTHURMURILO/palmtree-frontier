using Content.Shared._PS.Atmos.Components;

namespace Content.Client._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: client side of the temperature pump. Only refreshes the open window when the server
/// sends new state.
/// </summary>
public sealed class GasTemperaturePumpSystem : Content.Shared._PS.Atmos.EntitySystems.SharedGasTemperaturePumpSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasTemperaturePumpComponent, AfterAutoHandleStateEvent>(OnStateHandled);
    }

    private void OnStateHandled(Entity<GasTemperaturePumpComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateUi(ent);
    }

    protected override void UpdateUi(Entity<GasTemperaturePumpComponent> ent)
    {
        if (UserInterfaceSystem.TryGetOpenUi(ent.Owner, ent.Comp.UiKey, out var bui))
            bui.Update();
    }
}
