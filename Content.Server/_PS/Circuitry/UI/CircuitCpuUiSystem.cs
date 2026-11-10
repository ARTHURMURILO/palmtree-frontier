using System.Linq;
using System.Text;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Systems;
using Content.Shared._PS.Circuitry;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._PS.Circuitry.UI;

public sealed class CircuitCpuUiSystem : EntitySystem
{
    [Dependency] private readonly CircuitAssemblySystem _assembly = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LuaBrainSystem _lua = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private TimeSpan _nextUpdate;
    private readonly Dictionary<EntityUid, TimeSpan> _nextSave = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<CircuitCpuComponent>(CircuitCpuUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<CircuitCpuSaveMessage>(OnSave);
            subs.Event<CircuitCpuRunMessage>(OnRun);
            subs.Event<CircuitCpuStopMessage>(OnStop);
            subs.Event<CircuitCpuClearLogMessage>(OnClearLog);
        });

    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + TimeSpan.FromSeconds(0.5);

        var query = EntityQueryEnumerator<CircuitCpuComponent>();
        while (query.MoveNext(out var cpu, out var component))
        {
            if (_ui.IsUiOpen(cpu, CircuitCpuUiKey.Key))
                _ui.SetUiState(cpu, CircuitCpuUiKey.Key, BuildState(cpu, component, FindAssembly(cpu)));
        }

        var assemblyQuery = EntityQueryEnumerator<CircuitAssemblyComponent>();
        while (assemblyQuery.MoveNext(out var assembly, out var component))
        {
            if (_ui.IsUiOpen(assembly, CircuitCpuUiKey.Key))
                _ui.SetUiState(assembly, CircuitCpuUiKey.Key, BuildAssemblyState(assembly, component));
        }
    }

    private void OnOpened(EntityUid uid, CircuitCpuComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component, includeSource: true);
    }

    private void OnSave(EntityUid uid, CircuitCpuComponent component, CircuitCpuSaveMessage args)
    {
        if (!TryBeginSave(uid))
            return;

        if (Encoding.UTF8.GetByteCount(args.Source) > component.MaxSourceBytes)
        {
            _ui.SetUiState(uid, CircuitCpuUiKey.Key, BuildState(uid, component, FindAssembly(uid),
                Loc.GetString("circuit-cpu-ui-too-large", ("max", component.MaxSourceBytes)),
                includeSource: true));
            return;
        }

        component.Source = args.Source;
        component.LastEditor = MetaData(args.Actor).EntityName;

        var assembly = FindAssembly(uid);
        if (assembly is { } assemblyUid)
            _lua.Stop(assemblyUid, remove: true);

        UpdateUi(uid, component, includeSource: true);
    }

    private void OnRun(EntityUid uid, CircuitCpuComponent component, CircuitCpuRunMessage args)
    {
        if (args.Source != null &&
            !TryStoreRunSource(uid, component, args.Source, args.Actor, out var sourceError))
        {
            _ui.SetUiState(uid, CircuitCpuUiKey.Key, BuildState(uid, component, FindAssembly(uid),
                sourceError, includeSource: true));
            return;
        }

        if (FindAssembly(uid) is not { } assembly)
        {
            _ui.SetUiState(uid, CircuitCpuUiKey.Key, BuildState(uid, component, null,
                Loc.GetString("circuit-cpu-ui-not-installed")));
            return;
        }

        if (!_lua.Start(assembly))
        {
            _ui.SetUiState(uid, CircuitCpuUiKey.Key, BuildState(uid, component, assembly,
                Loc.GetString("circuit-cpu-ui-cannot-start")));
            return;
        }

        UpdateUi(uid, component, includeSource: true);
    }

    private void OnStop(EntityUid uid, CircuitCpuComponent component, CircuitCpuStopMessage args)
    {
        if (FindAssembly(uid) is { } assembly)
            _lua.Stop(assembly);

        UpdateUi(uid, component);
    }

    private void OnClearLog(EntityUid uid, CircuitCpuComponent component, CircuitCpuClearLogMessage args)
    {
        if (FindAssembly(uid) is { } assembly)
            _lua.ClearLog(assembly);

        UpdateUi(uid, component);
    }

    public void UpdateUi(EntityUid cpu, CircuitCpuComponent? component = null, string? extraError = null,
        bool includeSource = false)
    {
        if (!Resolve(cpu, ref component, false))
            return;

        _ui.SetUiState(cpu, CircuitCpuUiKey.Key,
            BuildState(cpu, component, FindAssembly(cpu), extraError, includeSource));
    }

    private CircuitCpuState BuildState(
        EntityUid cpu,
        CircuitCpuComponent component,
        EntityUid? assembly,
        string? extraError = null,
        bool includeSource = false)
    {
        var state = new CircuitCpuState
        {
            Source = includeSource ? component.Source : null,
            MaxSourceBytes = component.MaxSourceBytes,
            Installed = assembly != null,
            Error = extraError,
        };

        if (assembly is { } assemblyUid &&
            TryComp<CircuitAssemblyComponent>(assemblyUid, out var assemblyComponent))
        {
            state.Powered = assemblyComponent.Powered;
            state.Locked = assemblyComponent.Locked;
            state.AssemblyName = MetaData(assemblyUid).EntityName;

            if (_assembly.TryGetPower(assemblyUid, out var current, out var max))
            {
                state.Power = current;
                state.MaxPower = max;
            }

            var runtime = _lua.GetRuntime(assemblyUid);
            state.Running = runtime?.Program.IsRunning ?? false;
            state.Error ??= runtime?.Program.LastError;

            if (runtime != null)
                state.Log = runtime.Program.Log.ToList();
        }

        return state;
    }


    public void OnAssemblySave(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuSaveMessage args)
    {
        if (component.Cpu is not { } cpu || !TryComp<CircuitCpuComponent>(cpu, out var cpuComponent))
            return;

        if (!TryBeginSave(cpu))
            return;

        if (Encoding.UTF8.GetByteCount(args.Source) > cpuComponent.MaxSourceBytes)
        {
            UpdateAssemblyUi(uid, component,
                Loc.GetString("circuit-cpu-ui-too-large", ("max", cpuComponent.MaxSourceBytes)),
                includeSource: true);
            return;
        }

        cpuComponent.Source = args.Source;
        cpuComponent.LastEditor = MetaData(args.Actor).EntityName;
        _lua.Stop(uid, remove: true);
        UpdateAssemblyUi(uid, component, includeSource: true);
    }

    public void OnAssemblyRun(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuRunMessage args)
    {
        if (component.Cpu is not { } cpu || !TryComp<CircuitCpuComponent>(cpu, out var cpuComponent))
        {
            UpdateAssemblyUi(uid, component, Loc.GetString("circuit-cpu-ui-no-cpu"));
            return;
        }

        if (args.Source != null &&
            !TryStoreRunSource(cpu, cpuComponent, args.Source, args.Actor, out var sourceError))
        {
            UpdateAssemblyUi(uid, component, sourceError, includeSource: true);
            return;
        }

        if (!_lua.Start(uid))
        {
            UpdateAssemblyUi(uid, component, Loc.GetString("circuit-cpu-ui-cannot-start"));
            return;
        }

        UpdateAssemblyUi(uid, component, includeSource: true);
    }

    private bool TryStoreRunSource(
        EntityUid cpu,
        CircuitCpuComponent component,
        string source,
        EntityUid actor,
        out string? error)
    {
        if (Encoding.UTF8.GetByteCount(source) > component.MaxSourceBytes)
        {
            error = Loc.GetString("circuit-cpu-ui-too-large", ("max", component.MaxSourceBytes));
            return false;
        }

        component.Source = source;
        if (actor.Valid && Exists(actor))
            component.LastEditor = MetaData(actor).EntityName;

        error = null;
        return true;
    }

    public void OnAssemblyStop(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuStopMessage args)
    {
        _lua.Stop(uid);
        UpdateAssemblyUi(uid, component);
    }

    public void OnAssemblyClearLog(EntityUid uid, CircuitAssemblyComponent component, CircuitCpuClearLogMessage args)
    {
        _lua.ClearLog(uid);
        UpdateAssemblyUi(uid, component);
    }

    public void UpdateAssemblyUi(EntityUid assembly, CircuitAssemblyComponent? component = null,
        string? extraError = null, bool includeSource = false)
    {
        if (!Resolve(assembly, ref component, false))
            return;

        _ui.SetUiState(assembly, CircuitCpuUiKey.Key,
            BuildAssemblyState(assembly, component, extraError, includeSource));
    }

    private CircuitCpuState BuildAssemblyState(
        EntityUid assembly,
        CircuitAssemblyComponent component,
        string? extraError = null,
        bool includeSource = false)
    {
        if (component.Cpu is not { } cpu || !TryComp<CircuitCpuComponent>(cpu, out var cpuComponent))
        {
            return new CircuitCpuState
            {
                Installed = false,
                Error = extraError ?? Loc.GetString("circuit-cpu-ui-no-cpu"),
            };
        }

        return BuildState(cpu, cpuComponent, assembly, extraError, includeSource);
    }

    private bool TryBeginSave(EntityUid cpu)
    {
        var now = _timing.CurTime;
        if (_nextSave.TryGetValue(cpu, out var next) && now < next)
            return false;

        _nextSave[cpu] = now + TimeSpan.FromSeconds(1);
        return true;
    }

    private EntityUid? FindAssembly(EntityUid cpu)
    {
        if (!_container.TryGetOuterContainer(cpu, Transform(cpu), out var container))
            return null;

        return HasComp<CircuitAssemblyComponent>(container.Owner) ? container.Owner : null;
    }
}
