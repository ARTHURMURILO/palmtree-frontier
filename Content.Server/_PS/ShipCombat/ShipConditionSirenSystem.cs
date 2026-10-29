using Content.Server.Power.Components;
using Content.Shared._PS.ShipCombat;
using Content.Shared._PS.ShipCombat.Components;
using Content.Shared.Audio;
using Content.Shared.Power;
using Robust.Server.GameObjects;

namespace Content.Server._PS.ShipCombat;

/// <summary>
/// Palmtree: drives <see cref="ShipConditionSirenComponent"/> devices from the grid's condition.
/// </summary>
/// <remarks>
/// The siren is a strong rotating strobe light plus a looping ambient alarm. It turns on while the
/// grid is at <see cref="ShipConditionSirenComponent.ActiveCondition"/> (RED by default) and has
/// power, and turns off otherwise. The rotating light follows the point light toggle automatically.
/// </remarks>
public sealed class ShipConditionSirenSystem : EntitySystem
{
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly PointLightSystem _pointLight = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipConditionSirenComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipConditionSirenComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<ShipCombatConditionChangedEvent>(OnConditionChanged);
    }

    private void OnMapInit(EntityUid uid, ShipConditionSirenComponent component, MapInitEvent args)
    {
        UpdateSiren(uid, component);
    }

    private void OnPowerChanged(EntityUid uid, ShipConditionSirenComponent component, ref PowerChangedEvent args)
    {
        UpdateSiren(uid, component);
    }

    private void OnConditionChanged(ShipCombatConditionChangedEvent args)
    {
        var query = EntityQueryEnumerator<ShipConditionSirenComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var siren, out var xform))
        {
            if (xform.GridUid != args.Grid)
                continue;

            UpdateSiren(uid, siren);
        }
    }

    /// <summary>
    /// Turns the siren on when its grid is at the active condition and the siren has power.
    /// </summary>
    private void UpdateSiren(EntityUid uid, ShipConditionSirenComponent siren)
    {
        var condition = TryComp<ShipCombatComponent>(Transform(uid).GridUid, out var combat)
            ? combat.Condition
            : ShipCondition.Green;

        var powered = !TryComp<ApcPowerReceiverComponent>(uid, out var receiver) || receiver.Powered;
        var active = powered && condition == siren.ActiveCondition;

        _appearance.SetData(uid, ShipConditionSirenVisuals.Active, active);
        _pointLight.SetEnabled(uid, active);
        _ambient.SetAmbience(uid, active);
    }
}
