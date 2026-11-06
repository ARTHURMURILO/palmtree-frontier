using Content.Shared._PS.Atmos.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.UserInterface;

namespace Content.Shared._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: shared logic for the temperature pump. Handles the UI messages, examine text and the
/// on/off appearance; the heat transfer itself lives in the server system.
/// </summary>
public abstract class SharedGasTemperaturePumpSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _receiver = default!;
    [Dependency] protected readonly SharedUserInterfaceSystem UserInterfaceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasTemperaturePumpComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GasTemperaturePumpComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<GasTemperaturePumpComponent, AtmosDeviceDisabledEvent>(OnLeaveAtmosphere);
        SubscribeLocalEvent<GasTemperaturePumpComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<GasTemperaturePumpComponent, GasTemperaturePumpToggleMessage>(OnToggleMessage);
        SubscribeLocalEvent<GasTemperaturePumpComponent, GasTemperaturePumpChangeRateMessage>(OnChangeRateMessage);
    }

    private void OnInit(Entity<GasTemperaturePumpComponent> ent, ref ComponentInit args)
    {
        UpdateAppearance(ent);
    }

    private void OnPowerChanged(Entity<GasTemperaturePumpComponent> ent, ref PowerChangedEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnLeaveAtmosphere(Entity<GasTemperaturePumpComponent> ent, ref AtmosDeviceDisabledEvent args)
    {
        ent.Comp.Enabled = false;
        Dirty(ent);
        UpdateAppearance(ent);
        UserInterfaceSystem.CloseUi(ent.Owner, ent.Comp.UiKey);
    }

    private void OnExamined(Entity<GasTemperaturePumpComponent> ent, ref ExaminedEvent args)
    {
        if (!Transform(ent).Anchored || !args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(GasTemperaturePumpComponent)))
        {
            args.PushMarkup(Loc.GetString("gas-temperature-pump-examined-status",
                ("statusColor", ent.Comp.Enabled ? "green" : "red"),
                ("enabled", ent.Comp.Enabled)));

            args.PushMarkup(Loc.GetString("gas-temperature-pump-examined-rate",
                ("rate", $"{ent.Comp.HeatTransferRate:0.#}")));
        }
    }

    private void OnToggleMessage(Entity<GasTemperaturePumpComponent> ent, ref GasTemperaturePumpToggleMessage args)
    {
        ent.Comp.Enabled = args.Enabled;
        _adminLogger.Add(LogType.AtmosPowerChanged,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the power on {ToPrettyString(ent):device} to {args.Enabled}");
        Dirty(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnChangeRateMessage(Entity<GasTemperaturePumpComponent> ent, ref GasTemperaturePumpChangeRateMessage args)
    {
        ent.Comp.HeatTransferRate = Math.Clamp(args.Rate, 0f, ent.Comp.MaxHeatTransferRate);
        _adminLogger.Add(LogType.AtmosVolumeChanged,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the transfer rate on {ToPrettyString(ent):device} to {ent.Comp.HeatTransferRate}%");
        Dirty(ent);
        UpdateUi(ent);
    }

    public void UpdateAppearance(Entity<GasTemperaturePumpComponent, AppearanceComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp2, false))
            return;

        var running = ent.Comp1.Enabled && _receiver.IsPowered(ent.Owner);
        _appearance.SetData(ent, PumpVisuals.Enabled, running, ent.Comp2);
    }

    protected virtual void UpdateUi(Entity<GasTemperaturePumpComponent> ent)
    {
    }
}
