using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Shared._PS.Atmos.Components;
using Content.Shared._PS.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Audio;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Server._PS.Atmos.EntitySystems;

/// <summary>
/// Palmtree: runs the temperature pump. Heat is moved from the inlet pipeline to the outlet pipeline
/// without moving any gas, one way only.
/// </summary>
[UsedImplicitly]
public sealed class GasTemperaturePumpSystem : SharedGasTemperaturePumpSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasTemperaturePumpComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasTemperaturePumpComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnMapInit(Entity<GasTemperaturePumpComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUiUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
    }

    /// <summary>
    /// Refreshes the networked sensor readings used by the UI and by examine.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GasTemperaturePumpComponent>();

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
                nameof(comp.TransferredHeat));
        }
    }

    private void OnAtmosUpdate(Entity<GasTemperaturePumpComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        ent.Comp.TransferredHeat = 0f;
        _ambientSound.SetAmbience(ent, false);

        if (!_nodeContainer.TryGetNodes(ent.Owner, ent.Comp.InletName, ent.Comp.OutletName,
                out PipeNode? inlet, out PipeNode? outlet))
        {
            SetReadings(ent, null, null);
            return;
        }

        var inletAir = inlet.Air;
        var outletAir = outlet.Air;

        SetReadings(ent, inletAir, outletAir);

        if (ReferenceEquals(inletAir, outletAir) || inletAir.Immutable || outletAir.Immutable)
            return;

        if (!ent.Comp.Enabled || !_power.IsPowered(ent.Owner))
            return;

        var inletTemperature = inletAir.Temperature;
        var outletTemperature = outletAir.Temperature;

        // One way only: heat always travels from the hotter pipeline to the colder one.
        if (inletTemperature <= outletTemperature)
            return;

        var inletHeatCapacity = _atmosphere.GetHeatCapacity(inletAir, true);
        var outletHeatCapacity = _atmosphere.GetHeatCapacity(outletAir, true);
        var combinedHeatCapacity = inletHeatCapacity + outletHeatCapacity;

        if (combinedHeatCapacity <= Atmospherics.MinimumHeatCapacity)
            return;

        // The temperature both pipelines would settle on if they were allowed to fully equalise.
        var equilibrium = (inletTemperature * inletHeatCapacity + outletTemperature * outletHeatCapacity) /
                          combinedHeatCapacity;

        // Move that fraction of the way to equilibrium this tick. Scaling by dt keeps the device
        // tick-rate independent, and moving both sides by the same fraction of their distance to
        // equilibrium conserves energy exactly.
        var fraction = Math.Clamp(ent.Comp.HeatTransferRate * 0.01f * args.dt, 0f, 1f);

        if (fraction <= 0f)
            return;

        inletAir.Temperature = inletTemperature + (equilibrium - inletTemperature) * fraction;
        outletAir.Temperature = outletTemperature + (equilibrium - outletTemperature) * fraction;

        // Heat that left the inlet pipeline, in joules per second.
        ent.Comp.TransferredHeat = inletHeatCapacity * (inletTemperature - inletAir.Temperature) / args.dt;
        _ambientSound.SetAmbience(ent, ent.Comp.TransferredHeat > 0f);
    }

    private void SetReadings(Entity<GasTemperaturePumpComponent> ent, GasMixture? inletAir, GasMixture? outletAir)
    {
        ent.Comp.InletTemperature = inletAir?.Temperature ?? 0f;
        ent.Comp.OutletTemperature = outletAir?.Temperature ?? 0f;
    }
}
