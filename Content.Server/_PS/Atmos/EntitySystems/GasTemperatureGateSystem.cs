using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Shared._PS.Atmos;
using Content.Shared._PS.Atmos.Components;
using Content.Shared._PS.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Audio;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Server._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: runs the temperature gate. Gas is released from the inlet pipeline into the outlet
/// pipeline while the temperature condition is met, rate limited like the vanilla pressure regulator.
/// </summary>
[UsedImplicitly]
public sealed class GasTemperatureGateSystem : SharedGasTemperatureGateSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasTemperatureGateComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasTemperatureGateComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnMapInit(Entity<GasTemperatureGateComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUiUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
    }

    /// <summary>
    /// Refreshes the networked sensor readings used by the UI and by examine.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GasTemperatureGateComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextUiUpdate > _timing.CurTime)
                continue;

            comp.NextUiUpdate += comp.UpdateInterval;

            DirtyFields(uid,
                comp,
                null,
                nameof(comp.InletTemperature),
                nameof(comp.OutletTemperature),
                nameof(comp.FlowRate));
        }
    }

    private void OnAtmosUpdate(Entity<GasTemperatureGateComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNodes(ent.Owner, ent.Comp.InletName, ent.Comp.OutletName,
                out PipeNode? inlet, out PipeNode? outlet))
        {
            _ambientSound.SetAmbience(ent, false);
            SetReadings(ent, null, null, 0f);
            SetState(ent, TemperatureGateState.Off);
            return;
        }

        var inletAir = inlet.Air;
        var outletAir = outlet.Air;

        // Both nodes can end up on the same pipeline if the device is looped. Nothing to do then.
        if (ReferenceEquals(inletAir, outletAir))
        {
            _ambientSound.SetAmbience(ent, false);
            SetReadings(ent, inletAir, outletAir, 0f);
            SetState(ent, TemperatureGateState.Off);
            return;
        }

        if (!ent.Comp.Enabled || !_power.IsPowered(ent.Owner))
        {
            _ambientSound.SetAmbience(ent, false);
            SetReadings(ent, inletAir, outletAir, 0f);
            SetState(ent, TemperatureGateState.Off);
            return;
        }

        var temperature = inletAir.Temperature;
        var conditionMet = ent.Comp.Inverted
            ? temperature > ent.Comp.TargetTemperature
            : temperature < ent.Comp.TargetTemperature;

        if (!conditionMet || inletAir.Pressure <= 0f || outletAir.Pressure >= inletAir.Pressure)
        {
            _ambientSound.SetAmbience(ent, false);
            SetReadings(ent, inletAir, outletAir, 0f);
            SetState(ent, TemperatureGateState.On);
            return;
        }

        var dt = args.dt;

        // Only move as much gas as it takes to equalise the two pipelines...
        var molesToEqualize = _atmosphere.FractionToEqualizePressure(inletAir, outletAir) * inletAir.TotalMoles;
        var volumeToEqualize = molesToEqualize * (Atmospherics.R * temperature / inletAir.Pressure);

        // ...and no more than the flow rate allows this tick.
        var volumeToTransfer = Math.Min(volumeToEqualize,
            ent.Comp.MaxTransferRate * _atmosphere.PumpSpeedup() * dt);

        if (volumeToTransfer <= 0f)
        {
            _ambientSound.SetAmbience(ent, false);
            SetReadings(ent, inletAir, outletAir, 0f);
            SetState(ent, TemperatureGateState.On);
            return;
        }

        var removed = inletAir.RemoveVolume(volumeToTransfer);
        _atmosphere.Merge(outletAir, removed);

        SetReadings(ent, inletAir, outletAir, volumeToTransfer / dt);
        _ambientSound.SetAmbience(ent, true);
        SetState(ent, TemperatureGateState.Flowing);
    }

    /// <summary>
    /// Stores the latest sensor readings. They are networked by the once-a-second <see cref="Update"/>
    /// loop, not here, so this never dirties the entity on the atmos tick.
    /// </summary>
    private void SetReadings(Entity<GasTemperatureGateComponent> ent,
        GasMixture? inletAir,
        GasMixture? outletAir,
        float flowRate)
    {
        ent.Comp.InletTemperature = inletAir?.Temperature ?? 0f;
        ent.Comp.OutletTemperature = outletAir?.Temperature ?? 0f;
        ent.Comp.FlowRate = flowRate;
    }
}
