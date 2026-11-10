using System.Linq;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared._PS.Circuitry;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Radio.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._PS.Circuitry.UI;

public sealed class CircuitAssemblyUiSystem : EntitySystem
{
    [Dependency] private readonly CircuitAssemblySystem _assembly = default!;
    [Dependency] private readonly CircuitCpuUiSystem _cpuUi = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly LuaBrainSystem _lua = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        // This must be subscribed once for the component rather than through two BuiEvents blocks,
        // because opening the assembly panel and opening the installed CPU editor share the same
        // component and Robust rejects duplicate BoundUIOpenedEvent subscriptions.
        SubscribeLocalEvent<CircuitAssemblyComponent, BoundUIOpenedEvent>(OnOpened);

        Subs.BuiEvents<CircuitAssemblyComponent>(CircuitAssemblyUiKey.Key, subs =>
        {
            subs.Event<CircuitAssemblyEjectMessage>(OnEject);
            subs.Event<CircuitAssemblyRenameMessage>(OnRename);
            subs.Event<CircuitAssemblyLockMessage>(OnLock);
            subs.Event<CircuitAssemblyOpenCpuMessage>(OnOpenCpu);
            subs.Event<CircuitAssemblyRunMessage>(OnRun);
            subs.Event<CircuitAssemblyStopMessage>(OnStop);
        });

        Subs.BuiEvents<CircuitAssemblyComponent>(CircuitCpuUiKey.Key, subs =>
        {
            subs.Event<CircuitCpuSaveMessage>(OnCpuSave);
            subs.Event<CircuitCpuRunMessage>(OnCpuRun);
            subs.Event<CircuitCpuStopMessage>(OnCpuStop);
            subs.Event<CircuitCpuClearLogMessage>(OnCpuClearLog);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + TimeSpan.FromSeconds(0.5);

        var query = EntityQueryEnumerator<CircuitAssemblyComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (_ui.IsUiOpen(uid, CircuitAssemblyUiKey.Key))
                _ui.SetUiState(uid, CircuitAssemblyUiKey.Key, BuildState(uid, component));
        }
    }

    private void OnOpened(EntityUid uid, CircuitAssemblyComponent component, BoundUIOpenedEvent args)
    {
        if (Equals(args.UiKey, CircuitCpuUiKey.Key))
            _cpuUi.UpdateAssemblyUi(uid, component, includeSource: true);
        else
            UpdateUi(uid, component);
    }

    private void OnCpuSave(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuSaveMessage args)
    {
        if (_assembly.CanModify(uid, args.Actor, component))
            _cpuUi.OnAssemblySave(uid, component, args);
    }

    private void OnCpuRun(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuRunMessage args)
    {
        if (_assembly.CanModify(uid, args.Actor, component))
            _cpuUi.OnAssemblyRun(uid, component, args);
    }

    private void OnCpuStop(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuStopMessage args)
    {
        if (_assembly.CanModify(uid, args.Actor, component))
            _cpuUi.OnAssemblyStop(uid, component, args);
    }

    private void OnCpuClearLog(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuClearLogMessage args)
    {
        if (_assembly.CanModify(uid, args.Actor, component))
            _cpuUi.OnAssemblyClearLog(uid, component, args);
    }

    private void OnEject(EntityUid uid, CircuitAssemblyComponent component, CircuitAssemblyEjectMessage args)
    {
        if (!_assembly.CanModify(uid, args.Actor, component))
            return;

        _itemSlots.TryEject(uid, args.SlotId, args.Actor, out _);
        UpdateUi(uid, component);
    }

    private void OnRename(EntityUid uid, CircuitAssemblyComponent component, CircuitAssemblyRenameMessage args)
    {
        if (!_assembly.CanModify(uid, args.Actor, component))
            return;

        var name = args.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
            return;

        _metaData.SetEntityName(uid, name);
        UpdateUi(uid, component);
    }

    private void OnLock(EntityUid uid, CircuitAssemblyComponent component, CircuitAssemblyLockMessage args)
    {
        if (!_assembly.TrySetLocked(uid, args.Actor, args.Locked, component))
            return;

        UpdateUi(uid, component);
        _cpuUi.UpdateAssemblyUi(uid, component);
    }

    private void OnOpenCpu(EntityUid uid, CircuitAssemblyComponent component, CircuitAssemblyOpenCpuMessage args)
    {
        if (component.Cpu == null || !_assembly.CanModify(uid, args.Actor, component))
            return;

        _ui.TryOpenUi(uid, CircuitCpuUiKey.Key, args.Actor);
    }

    private void OnRun(EntityUid uid, CircuitAssemblyComponent component, CircuitAssemblyRunMessage args)
    {
        if (!_assembly.CanModify(uid, args.Actor, component))
            return;

        _lua.Start(uid);
        UpdateUi(uid, component);
    }

    private void OnStop(EntityUid uid, CircuitAssemblyComponent component, CircuitAssemblyStopMessage args)
    {
        if (!_assembly.CanModify(uid, args.Actor, component))
            return;

        _lua.Stop(uid);
        UpdateUi(uid, component);
    }

    public void UpdateUi(EntityUid uid, CircuitAssemblyComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        _ui.SetUiState(uid, CircuitAssemblyUiKey.Key, BuildState(uid, component));
    }

    private CircuitAssemblyState BuildState(EntityUid uid, CircuitAssemblyComponent component)
    {
        var state = new CircuitAssemblyState
        {
            Name = MetaData(uid).EntityName,
            Locked = component.Locked,
            OwnerName = component.OwnerName,
            Hands = component.Hands,
            SensorRange = component.SensorRange,
            HasVoice = component.Capabilities.HasFlag(CircuitCapability.Voice),
            HasLocomotion = component.Capabilities.HasFlag(CircuitCapability.Locomotion),
            HasCpu = component.Cpu != null,
            Powered = component.Powered,
        };

        if (_assembly.TryGetPower(uid, out var current, out var max))
        {
            state.Power = current;
            state.MaxPower = max;
        }

        if (component.Cpu is { } cpu)
            state.CpuName = MetaData(cpu).EntityName;

        if (component.Radio is { } radio && TryComp<EncryptionKeyHolderComponent>(radio, out var keys))
            state.RadioChannels = keys.Channels.OrderBy(channel => channel, StringComparer.Ordinal).ToList();

        var runtime = _lua.GetRuntime(uid);
        state.Running = runtime?.Program.IsRunning ?? false;
        state.Error = runtime?.Program.LastError;

        foreach (var (slotId, role) in component.Slots)
        {
            var entry = new CircuitSlotState
            {
                SlotId = slotId,
                Role = role.ToString(),
            };

            if (_itemSlots.TryGetSlot(uid, slotId, out var slot) && slot.Item is { } item)
                entry.ItemName = MetaData(item).EntityName;

            state.Slots.Add(entry);
        }

        return state;
    }
}
