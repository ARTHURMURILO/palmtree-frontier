using System.Diagnostics;
using System.Linq;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Circuitry.Lua;
using Content.Shared._PS.CCVar;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Stopwatch = System.Diagnostics.Stopwatch;
namespace Content.Server._PS.Circuitry.Systems;

/// <summary>
/// Palmtree: schedules one sandboxed Lua program per active assembly. Programs are suspended, not
/// destroyed, when power is lost, the map is paused or the chassis dies.
/// </summary>
public sealed class LuaBrainSystem : EntitySystem
{
    private static readonly TimeSpan MaxTotalLuaTime = TimeSpan.FromMilliseconds(2.5);

    [Dependency] private readonly CircuitLuaApiSystem _luaApi = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    private readonly Dictionary<EntityUid, CircuitLuaRuntime> _runtimes = new();

    public IReadOnlyCollection<CircuitLuaRuntime> Runtimes => _runtimes.Values;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CircuitCpuComponent, ComponentShutdown>(OnCpuShutdown);
    }

    private void OnCpuShutdown(EntityUid uid, CircuitCpuComponent component, ComponentShutdown args)
    {
        foreach (var assembly in _runtimes.Where(pair => pair.Value.Cpu == uid).Select(pair => pair.Key).ToList())
            RemoveRuntime(assembly);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_cfg.GetCVar(PSCVars.CircuitryLuaEnabled))
        {
            if (_runtimes.Count == 0)
                return;

            foreach (var runtime in _runtimes.Values)
                runtime.Program.Stop();

            _runtimes.Clear();
            return;
        }

        var watch = Stopwatch.StartNew();
        List<EntityUid>? remove = null;

        foreach (var (assembly, runtime) in _runtimes)
        {
            if (watch.Elapsed >= MaxTotalLuaTime)
                break;

            if (!TryComp<CircuitAssemblyComponent>(assembly, out var component) ||
                component.Cpu != runtime.Cpu ||
                !TryComp<CircuitCpuComponent>(runtime.Cpu, out _))
            {
                remove ??= new List<EntityUid>();
                remove.Add(assembly);
                continue;
            }

            if (!component.Powered || Paused(assembly) || _mobState.IsDead(assembly))
                continue;

            runtime.Program.Update(_timing.CurTime);
        }

        if (remove == null)
            return;

        foreach (var assembly in remove)
            RemoveRuntime(assembly);
    }

    public bool Start(EntityUid assembly)
    {
        if (!_cfg.GetCVar(PSCVars.CircuitryLuaEnabled))
            return false;

        if (!TryComp<CircuitAssemblyComponent>(assembly, out var component) ||
            component.Cpu is not { } cpuEntity ||
            !component.Powered ||
            !TryComp<CircuitCpuComponent>(cpuEntity, out var cpu))
        {
            return false;
        }

        RemoveRuntime(assembly);

        var runtime = new CircuitLuaRuntime
        {
            Assembly = assembly,
            Cpu = cpuEntity,
        };

        var options = new LuaProgramOptions
        {
            MaxSourceBytes = cpu.MaxSourceBytes,
            AutoYieldInstructions = cpu.AutoYieldInstructions,
            TickBudget = TimeSpan.FromMilliseconds(cpu.TickBudgetMilliseconds),
            MaxNoWaitTime = TimeSpan.FromSeconds(5),
            MaxWait = TimeSpan.FromHours(1),
            MaxLogLines = 100,
            MaxStringBytes = 1024 * 1024,
        };

        runtime.Program = new LuaProgram(
            cpu.Source,
            options,
            null,
            (script, robot) => _luaApi.Configure(script, robot, runtime));

        _runtimes[assembly] = runtime;
        return runtime.Program.Start(_timing.CurTime);
    }

    public void Stop(EntityUid assembly, bool remove = false)
    {
        if (!_runtimes.TryGetValue(assembly, out var runtime))
            return;

        runtime.Program.Stop();

        if (remove)
            _runtimes.Remove(assembly);
    }

    public void RemoveRuntime(EntityUid assembly)
    {
        if (!_runtimes.Remove(assembly, out var runtime))
            return;

        runtime.Program.Stop();
    }

    public CircuitLuaRuntime? GetRuntime(EntityUid assembly)
    {
        return _runtimes.GetValueOrDefault(assembly);
    }

    public void ClearLog(EntityUid assembly)
    {
        GetRuntime(assembly)?.Program.ClearLog();
    }
}
