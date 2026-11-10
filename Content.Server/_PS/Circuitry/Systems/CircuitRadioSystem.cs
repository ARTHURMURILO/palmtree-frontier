using Content.Server._PS.Circuitry.Components;
using Content.Server.Radio;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;

namespace Content.Server._PS.Circuitry.Systems;

/// <summary>
/// Palmtree: relays radio traffic heard by the assembly's radio module into Lua events and keeps
/// the passive radio listener in sync when the module's encryption keys change.
/// </summary>
public sealed class CircuitRadioSystem : EntitySystem
{
    [Dependency] private readonly CircuitAssemblySystem _assembly = default!;
    [Dependency] private readonly LuaBrainSystem _lua = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CircuitAssemblyComponent, RadioReceiveEvent>(OnRadioReceive);
        SubscribeLocalEvent<CircuitAssemblyComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<EncryptionKeyHolderComponent, EncryptionChannelsChangedEvent>(OnEncryptionChannelsChanged);
    }

    private void OnEncryptionChannelsChanged(
        EntityUid uid,
        EncryptionKeyHolderComponent component,
        EncryptionChannelsChangedEvent args)
    {
        var parent = Transform(uid).ParentUid;
        if (parent.IsValid() && TryComp<CircuitAssemblyComponent>(parent, out var assembly))
            _assembly.RefreshRadio(parent, assembly);
    }

    private void OnRadioReceive(EntityUid uid, CircuitAssemblyComponent component, ref RadioReceiveEvent args)
    {
        if (args.MessageSource == uid ||
            !component.Capabilities.HasFlag(CircuitCapability.Radio) ||
            component.Radio is not { } radio ||
            !component.Powered ||
            _mobState.IsDead(uid) ||
            !TryComp<EncryptionKeyHolderComponent>(radio, out var keys) ||
            !keys.Channels.Contains(args.Channel.ID))
        {
            return;
        }

        var runtime = _lua.GetRuntime(uid);
        if (runtime is null || !runtime.Program.IsRunning)
            return;

        runtime.QueueEvent(new CircuitEvent("radio", args.MessageSource, args.Message, args.Channel.ID));
    }

    private void OnSignalReceived(EntityUid uid, CircuitAssemblyComponent component, ref SignalReceivedEvent args)
    {
        if (!component.Capabilities.HasFlag(CircuitCapability.Radio) ||
            component.Radio is null ||
            !component.Powered ||
            _mobState.IsDead(uid))
        {
            return;
        }

        var runtime = _lua.GetRuntime(uid);
        if (runtime is null || !runtime.Program.IsRunning)
            return;

        var state = SignalState.Momentary;
        args.Data?.TryGetValue(DeviceNetworkConstants.LogicState, out state);

        runtime.QueueEvent(new CircuitEvent(
            "signal",
            args.Trigger ?? uid,
            args.Port,
            null,
            args.Port,
            state != SignalState.Low));
    }
}
