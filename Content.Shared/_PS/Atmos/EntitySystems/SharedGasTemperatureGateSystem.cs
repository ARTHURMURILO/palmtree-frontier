using Content.Shared._PS.Atmos.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.UserInterface;

namespace Content.Shared._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: shared logic for the temperature gate. Handles the UI messages, examine text and the
/// on/off appearance; the actual gas transfer lives in the server system.
/// </summary>
public abstract class SharedGasTemperatureGateSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _receiver = default!;
    [Dependency] protected readonly SharedUserInterfaceSystem UserInterfaceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasTemperatureGateComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GasTemperatureGateComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<GasTemperatureGateComponent, AtmosDeviceDisabledEvent>(OnLeaveAtmosphere);
        SubscribeLocalEvent<GasTemperatureGateComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<GasTemperatureGateComponent, GasTemperatureGateToggleMessage>(OnToggleMessage);
        SubscribeLocalEvent<GasTemperatureGateComponent, GasTemperatureGateChangeTemperatureMessage>(OnChangeTemperatureMessage);
        SubscribeLocalEvent<GasTemperatureGateComponent, GasTemperatureGateInvertMessage>(OnInvertMessage);
    }

    private void OnInit(Entity<GasTemperatureGateComponent> ent, ref ComponentInit args)
    {
        UpdateAppearance(ent);
    }

    private void OnPowerChanged(Entity<GasTemperatureGateComponent> ent, ref PowerChangedEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnLeaveAtmosphere(Entity<GasTemperatureGateComponent> ent, ref AtmosDeviceDisabledEvent args)
    {
        ent.Comp.Enabled = false;
        Dirty(ent);
        UpdateAppearance(ent);
        UserInterfaceSystem.CloseUi(ent.Owner, ent.Comp.UiKey);
    }

    private void OnExamined(Entity<GasTemperatureGateComponent> ent, ref ExaminedEvent args)
    {
        if (!Transform(ent).Anchored || !args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(GasTemperatureGateComponent)))
        {
            args.PushMarkup(Loc.GetString("gas-temperature-gate-examined-status",
                ("statusColor", ent.Comp.Enabled ? "green" : "red"),
                ("enabled", ent.Comp.Enabled)));

            args.PushMarkup(Loc.GetString("gas-temperature-gate-examined-temperature",
                ("temperature", $"{ent.Comp.TargetTemperature:0.#}"),
                ("direction", Loc.GetString(ent.Comp.Inverted
                    ? "gas-temperature-gate-examined-higher"
                    : "gas-temperature-gate-examined-lower"))));

            args.PushMarkup(Loc.GetString("gas-temperature-gate-examined-flow-rate",
                ("flowRate", $"{ent.Comp.FlowRate:0.#}")));
        }
    }

    private void OnToggleMessage(Entity<GasTemperatureGateComponent> ent, ref GasTemperatureGateToggleMessage args)
    {
        ent.Comp.Enabled = args.Enabled;
        _adminLogger.Add(LogType.AtmosPowerChanged,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the power on {ToPrettyString(ent):device} to {args.Enabled}");
        Dirty(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnChangeTemperatureMessage(Entity<GasTemperatureGateComponent> ent,
        ref GasTemperatureGateChangeTemperatureMessage args)
    {
        ent.Comp.TargetTemperature = Math.Clamp(args.Temperature, ent.Comp.MinTemperature, ent.Comp.MaxTemperature);
        _adminLogger.Add(LogType.AtmosTemperatureChanged,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the target temperature on {ToPrettyString(ent):device} to {ent.Comp.TargetTemperature}K");
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnInvertMessage(Entity<GasTemperatureGateComponent> ent, ref GasTemperatureGateInvertMessage args)
    {
        ent.Comp.Inverted = args.Inverted;
        _adminLogger.Add(LogType.AtmosDeviceSetting,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} set the inversion of {ToPrettyString(ent):device} to {args.Inverted}");
        Dirty(ent);
        UpdateUi(ent);
    }

    /// <summary>
    /// Picks the sprite state the gate should show right now. The server system overrides this with
    /// <see cref="TemperatureGateState.Flowing"/> while gas is actually moving.
    /// </summary>
    public void UpdateAppearance(Entity<GasTemperatureGateComponent, AppearanceComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp2, false))
            return;

        var state = ent.Comp1.Enabled && _receiver.IsPowered(ent.Owner)
            ? TemperatureGateState.On
            : TemperatureGateState.Off;

        SetState((ent.Owner, ent.Comp1), state);
    }

    /// <summary>
    /// Sets the sprite state of the gate.
    /// </summary>
    public void SetState(Entity<GasTemperatureGateComponent> ent, TemperatureGateState state)
    {
        if (!TryComp<AppearanceComponent>(ent, out var appearance))
            return;

        _appearance.SetData(ent, TemperatureGateVisuals.State, state, appearance);
    }

    protected virtual void UpdateUi(Entity<GasTemperatureGateComponent> ent)
    {
    }
}
