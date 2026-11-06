using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._PS.Atmos;
using Content.Shared._PS.Atmos.Components;
using Content.Shared._PS.Atmos.EntitySystems;
using Content.Shared.Atmos;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Server._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: keeps the atmos meter's dial and readings up to date with the pipeline it is attached to.
/// </summary>
[UsedImplicitly]
public sealed class GasMeterSystem : SharedGasMeterSystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasMeterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasMeterComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnMapInit(Entity<GasMeterComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
    }

    /// <summary>
    /// Refreshes the networked readings used by examine once per <see cref="GasMeterComponent.UpdateInterval"/>.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GasMeterComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextUpdate > _timing.CurTime)
                continue;

            comp.NextUpdate += comp.UpdateInterval;

            Dirty(uid, comp);
        }
    }

    private void OnAtmosUpdate(Entity<GasMeterComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNode(ent.Owner, ent.Comp.NodeName, out PipeNode? node))
            return;

        var air = node.Air;

        // An unconnected pipe node reports the shared space mixture; there is nothing to measure.
        if (air.Immutable)
            return;

        ent.Comp.Pressure = air.Pressure;
        ent.Comp.Temperature = air.Temperature;

        var pressureLevel = GetPressureLevel(air.Pressure);
        var temperatureLevel = GetTemperatureLevel(air.Temperature);

        if (pressureLevel == ent.Comp.PressureLevel && temperatureLevel == ent.Comp.TemperatureLevel)
            return;

        ent.Comp.PressureLevel = pressureLevel;
        ent.Comp.TemperatureLevel = temperatureLevel;

        _appearance.SetData(ent, GasMeterVisuals.Pressure, pressureLevel);
        _appearance.SetData(ent, GasMeterVisuals.Temperature, temperatureLevel);

        Dirty(ent);
    }

    /// <summary>
    /// Maps a pressure in kPa to one of the five dial states, using the same bands as /tg/station.
    /// </summary>
    public static GasMeterPressureLevel GetPressureLevel(float pressure)
    {
        if (pressure <= 0.15f * Atmospherics.OneAtmosphere)
            return GasMeterPressureLevel.Zero;

        if (pressure <= 1.8f * Atmospherics.OneAtmosphere)
            return GasMeterPressureLevel.Low;

        if (pressure <= 30f * Atmospherics.OneAtmosphere)
            return GasMeterPressureLevel.Medium;

        if (pressure <= 59f * Atmospherics.OneAtmosphere)
            return GasMeterPressureLevel.High;

        return GasMeterPressureLevel.Extreme;
    }

    /// <summary>
    /// Maps a temperature in kelvin to the colour band the dial is tinted with.
    /// </summary>
    public static GasMeterTemperatureLevel GetTemperatureLevel(float temperature)
    {
        return temperature switch
        {
            < 200f => GasMeterTemperatureLevel.Freezing,
            < 270f => GasMeterTemperatureLevel.Cold,
            < 330f => GasMeterTemperatureLevel.Normal,
            < 500f => GasMeterTemperatureLevel.Warm,
            < 900f => GasMeterTemperatureLevel.Hot,
            _ => GasMeterTemperatureLevel.Burning,
        };
    }
}
