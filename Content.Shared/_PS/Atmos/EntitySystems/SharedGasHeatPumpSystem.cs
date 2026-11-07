using Content.Shared._PS.Atmos.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Database;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: shared logic for the heat pump. Handles the UI messages, examine text, the body
/// appearance and the safeties; the heat exchange itself lives in the server system.
/// </summary>
public abstract class SharedGasHeatPumpSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] protected readonly SharedUserInterfaceSystem UserInterfaceSystem = default!;
    [Dependency] protected readonly SharedPowerReceiverSystem Receiver = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasHeatPumpComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GasHeatPumpComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<GasHeatPumpComponent, AtmosDeviceDisabledEvent>(OnLeaveAtmosphere);
        SubscribeLocalEvent<GasHeatPumpComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GasHeatPumpComponent, GotEmaggedEvent>(OnEmagged);

        SubscribeLocalEvent<GasHeatPumpComponent, GasHeatPumpToggleMessage>(OnToggleMessage);
        SubscribeLocalEvent<GasHeatPumpComponent, GasHeatPumpChangeTemperatureMessage>(OnChangeTemperatureMessage);
        SubscribeLocalEvent<GasHeatPumpComponent, GasHeatPumpToggleSafetiesMessage>(OnToggleSafetiesMessage);
    }

    private void OnInit(Entity<GasHeatPumpComponent> ent, ref ComponentInit args)
    {
        UpdateAppearance(ent);
    }

    private void OnPowerChanged(Entity<GasHeatPumpComponent> ent, ref PowerChangedEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnLeaveAtmosphere(Entity<GasHeatPumpComponent> ent, ref AtmosDeviceDisabledEvent args)
    {
        ent.Comp.Enabled = false;
        Dirty(ent);
        UpdateAppearance(ent);
        UserInterfaceSystem.CloseUi(ent.Owner, ent.Comp.UiKey);
    }

    private void OnEmagged(Entity<GasHeatPumpComponent> ent, ref GotEmaggedEvent args)
    {
        // /tg/station's heat pump: the emag "overwrites the thermal safety restrictions", which is what
        // lets the waste loop run away far enough to take the machine with it.
        if (!ent.Comp.Safeties)
            return;

        args.Handled = true;
        ent.Comp.Safeties = false;
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnExamined(Entity<GasHeatPumpComponent> ent, ref ExaminedEvent args)
    {
        if (!Transform(ent).Anchored || !args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(GasHeatPumpComponent)))
        {
            args.PushMarkup(Loc.GetString("gas-heat-pump-examined-status",
                ("statusColor", ent.Comp.Enabled ? "green" : "red"),
                ("enabled", ent.Comp.Enabled)));

            args.PushMarkup(Loc.GetString("gas-heat-pump-examined-mode",
                ("mode", Loc.GetString(ent.Comp.Cooling
                    ? "gas-heat-pump-mode-cooling"
                    : "gas-heat-pump-mode-heating"))));

            args.PushMarkup(Loc.GetString("gas-heat-pump-examined-temperature",
                ("temperature", $"{ent.Comp.TargetTemperature:0.#}")));

            args.PushMarkup(Loc.GetString("gas-heat-pump-examined-gas",
                ("temperature", $"{ent.Comp.GasTemperature:0.#}"),
                ("pressure", $"{ent.Comp.GasPressure:0.#}")));

            args.PushMarkup(Loc.GetString("gas-heat-pump-examined-waste",
                ("temperature", $"{ent.Comp.WasteTemperature:0.#}"),
                ("pressure", $"{ent.Comp.WastePressure:0.#}")));

            args.PushMarkup(Loc.GetString("gas-heat-pump-examined-efficiency",
                ("efficiency", $"{ent.Comp.Efficiency * 100f:0}")));

            if (!ent.Comp.Safeties)
                args.PushMarkup(Loc.GetString("gas-heat-pump-examined-safeties-off"));
        }
    }

    private void OnToggleMessage(Entity<GasHeatPumpComponent> ent, ref GasHeatPumpToggleMessage args)
    {
        ent.Comp.Enabled = args.Enabled;
        _adminLogger.Add(LogType.AtmosPowerChanged,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} turned {(args.Enabled ? "On" : "Off")} {ToPrettyString(ent):device}");
        Dirty(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnChangeTemperatureMessage(Entity<GasHeatPumpComponent> ent,
        ref GasHeatPumpChangeTemperatureMessage args)
    {
        // A malformed client message must not put NaN into the setpoint: it would survive the clamp
        // and then poison the gas mixture on the next atmos tick.
        if (!float.IsFinite(args.Temperature))
            return;

        ent.Comp.TargetTemperature = Math.Clamp(args.Temperature, ent.Comp.MinTemperature, ent.Comp.MaxTemperature);
        _adminLogger.Add(LogType.AtmosTemperatureChanged,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the target temperature on {ToPrettyString(ent):device} to {ent.Comp.TargetTemperature}K");
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnToggleSafetiesMessage(Entity<GasHeatPumpComponent> ent,
        ref GasHeatPumpToggleSafetiesMessage args)
    {
        ent.Comp.Safeties = args.Safeties;
        _adminLogger.Add(LogType.AtmosDeviceSetting,
            args.Safeties ? LogImpact.Low : LogImpact.High,
            $"{ToPrettyString(args.Actor):player} turned the thermal safeties {(args.Safeties ? "on" : "off")} on {ToPrettyString(ent):device}");
        Dirty(ent);
        UpdateUi(ent);
    }

    /// <summary>
    /// Pushes the current mode and run state to the sprite.
    /// </summary>
    public void UpdateAppearance(Entity<GasHeatPumpComponent> ent)
    {
        if (!TryComp(ent.Owner, out AppearanceComponent? appearance))
            return;

        // The running art means "working right now", so an enabled machine without power shows as off.
        var running = ent.Comp.Enabled && Receiver.IsPowered(ent.Owner);

        var state = (ent.Comp.Cooling, running) switch
        {
            (true, true) => GasHeatPumpVisualState.OnCooling,
            (true, false) => GasHeatPumpVisualState.OffCooling,
            (false, true) => GasHeatPumpVisualState.OnHeating,
            (false, false) => GasHeatPumpVisualState.OffHeating,
        };

        _appearance.SetData(ent.Owner, GasHeatPumpVisuals.State, state, appearance);
    }

    /// <summary>
    /// Refreshes an open window. The server has nothing to do; the client overrides this.
    /// </summary>
    protected virtual void UpdateUi(Entity<GasHeatPumpComponent> ent)
    {
    }
}
