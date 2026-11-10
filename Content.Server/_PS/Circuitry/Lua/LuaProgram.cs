using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MoonSharp.Interpreter;

namespace Content.Server._PS.Circuitry.Lua;

/// <summary>
/// Registers the host-provided functions exposed to a Lua program as the `robot` table.
/// </summary>
public delegate void LuaApiRegistrar(Script script, Table robot);

/// <summary>
/// A single sandboxed Lua program executed cooperatively on the server thread.
/// </summary>
/// <remarks>
/// The source is loaded as a chunk coroutine and run under MoonSharp's
/// <see cref="Coroutine.AutoYieldCounter"/> so a hostile or accidental infinite loop
/// cannot block the server tick. Once the chunk has defined the program's functions,
/// the global `main` function is extracted and run as a second coroutine.
///
/// `robot.wait(seconds)` returns a yield request; the scheduler resumes the coroutine
/// with the elapsed time when the wait expires. This gives scripts a simple blocking
/// programming model without ever blocking the server thread.
/// </remarks>
public sealed class LuaProgram
{
    private const string ChunkName = "cpu.lua";

    private readonly string _source;
    private readonly LuaProgramOptions _options;
    private readonly Action<string>? _logSink;
    private readonly LuaApiRegistrar? _configureApi;
    private readonly Queue<string> _log = new();
    private readonly Stopwatch _budgetWatch = new();

    private Script? _script;
    private Table? _robot;
    private Coroutine? _coroutine;
    private LuaProgramState _state = LuaProgramState.Idle;
    private bool _isChunk = true;
    private TimeSpan _waitStartedAt;
    private TimeSpan _wakeAt;
    private TimeSpan _lastWaitAt;
    private string? _lastError;
    private int _logVersion;

    public LuaProgram(
        string source,
        LuaProgramOptions? options = null,
        Action<string>? logSink = null,
        LuaApiRegistrar? configureApi = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? new LuaProgramOptions();
        _logSink = logSink;
        _configureApi = configureApi;
    }

    public string Source => _source;
    public LuaProgramOptions Options => _options;
    public LuaProgramState State => _state;
    public string? LastError => _lastError;
    public int LogVersion => _logVersion;
    public bool IsRunning => _state is LuaProgramState.Loading or LuaProgramState.Running or LuaProgramState.Waiting;
    public IReadOnlyList<string> Log => _log.ToArray();

    /// <summary>
    /// Parses the source and prepares the chunk coroutine. Returns false and sets
    /// <see cref="LastError"/> if the source is too large or fails to parse.
    /// </summary>
    public bool Start(TimeSpan now)
    {
        if (IsRunning)
            return false;

        if (Encoding.UTF8.GetByteCount(_source) > _options.MaxSourceBytes)
        {
            Fail($"Program source exceeds the {_options.MaxSourceBytes} byte limit.");
            return false;
        }

        _lastError = null;
        _log.Clear();
        _logVersion++;

        try
        {
            _script = LuaSandbox.CreateScript(_options, AddLog);
            _robot = new Table(_script);
            _robot.Set("log", DynValue.NewCallback(LogCallback, "log"));
            _robot.Set("wait", DynValue.NewCallback(WaitCallback, "wait"));

            _configureApi?.Invoke(_script, _robot);

            _script.Globals.Set("robot", DynValue.NewTable(_robot));
            _script.Globals.Set("print", DynValue.NewCallback(LogCallback, "print"));
            _script.Globals.Set("log", DynValue.NewCallback(LogCallback, "log"));

            var chunk = _script.LoadString(_source, null, ChunkName);
            if (chunk.Type is not (DataType.Function or DataType.ClrFunction))
            {
                Fail("Program source did not produce a Lua function.");
                return false;
            }

            _coroutine = _script.CreateCoroutine(chunk).Coroutine;
            _coroutine.AutoYieldCounter = _options.AutoYieldInstructions;
            _isChunk = true;
            _state = LuaProgramState.Loading;
            _waitStartedAt = now;
            _wakeAt = now;
            _lastWaitAt = now;
            return true;
        }
        catch (InterpreterException ex)
        {
            Fail(ex.DecoratedMessage ?? ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Advances the program. Call once per server tick with the current round time.
    /// </summary>
    public LuaProgramState Update(TimeSpan now)
    {
        if (_state is not (LuaProgramState.Loading or LuaProgramState.Running or LuaProgramState.Waiting))
            return _state;

        if (_coroutine == null)
        {
            Fail("Program has no active coroutine.");
            return _state;
        }

        if (_state == LuaProgramState.Waiting && now < _wakeAt)
            return _state;

        var resumeArgs = Array.Empty<DynValue>();
        if (_state == LuaProgramState.Waiting)
        {
            var elapsed = now - _waitStartedAt;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            resumeArgs = new[] { DynValue.NewNumber(elapsed.TotalSeconds) };
            _state = LuaProgramState.Running;
        }

        _budgetWatch.Restart();
        var resumed = false;

        do
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            DynValue result;

            try
            {
                result = _coroutine.Resume(resumeArgs);
            }
            catch (InterpreterException ex)
            {
                Fail(ex.DecoratedMessage ?? ex.Message);
                return _state;
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                return _state;
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (allocated > _options.MaxAllocationBytesPerResume)
            {
                Fail($"Program allocated {allocated} bytes in a single resume, exceeding the watchdog limit.");
                return _state;
            }

            resumeArgs = Array.Empty<DynValue>();
            resumed = true;

            // MoonSharp reports auto-yields as YieldRequest values, while an explicit
            // coroutine yield (our robot.wait) returns the yielded value directly and
            // leaves the coroutine Suspended. Check both signals.
            if (result.Type == DataType.YieldRequest && result.YieldRequest.Forced)
            {
                if (now - _lastWaitAt >= _options.MaxNoWaitTime)
                {
                    Fail("CPU budget exceeded: program ran without calling robot.wait().");
                    return _state;
                }

                continue;
            }

            if (_coroutine.State == CoroutineState.ForceSuspended)
            {
                if (now - _lastWaitAt >= _options.MaxNoWaitTime)
                {
                    Fail("CPU budget exceeded: program ran without calling robot.wait().");
                    return _state;
                }

                continue;
            }

            if (_coroutine.State == CoroutineState.Suspended)
            {
                var seconds = 0d;
                if (result.Type == DataType.Number)
                {
                    seconds = result.Number;
                }
                else if (result.Type == DataType.YieldRequest)
                {
                    var request = result.YieldRequest;
                    if (request.ReturnValues.Length > 0 && request.ReturnValues[0].Type == DataType.Number)
                        seconds = request.ReturnValues[0].Number;
                }
                else if (result.Type == DataType.Tuple &&
                         result.Tuple.Length > 0 &&
                         result.Tuple[0].Type == DataType.Number)
                {
                    seconds = result.Tuple[0].Number;
                }

                if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                    seconds = 0;

                seconds = Math.Min(seconds, _options.MaxWait.TotalSeconds);

                _waitStartedAt = now;
                _wakeAt = now + TimeSpan.FromSeconds(seconds);
                _lastWaitAt = now;
                _state = LuaProgramState.Waiting;
                return _state;
            }

            if (_coroutine.State != CoroutineState.Dead)
            {
                Fail($"Unexpected coroutine state: {_coroutine.State}.");
                return _state;
            }

            // The chunk has finished executing; its only job is to define globals.
            if (_isChunk)
            {
                _isChunk = false;
                var main = _script!.Globals.Get("main");
                if (main.Type is not (DataType.Function or DataType.ClrFunction))
                {
                    Fail("main() is not defined.");
                    return _state;
                }

                _coroutine = _script.CreateCoroutine(main).Coroutine;
                _coroutine.AutoYieldCounter = _options.AutoYieldInstructions;
                _state = LuaProgramState.Running;
                continue;
            }

            _state = LuaProgramState.Finished;
            _coroutine = null;
            _script = null;
            _robot = null;
            return _state;
        }
        while (!resumed || _budgetWatch.Elapsed < _options.TickBudget);

        return _state;
    }

    public void Stop()
    {
        if (_state is LuaProgramState.Finished or LuaProgramState.Error or LuaProgramState.Stopped)
            return;

        _state = LuaProgramState.Stopped;
        _coroutine = null;
        _script = null;
        _robot = null;
    }

    public void ClearLog()
    {
        _log.Clear();
        _logVersion++;
    }

    private DynValue LogCallback(ScriptExecutionContext context, CallbackArguments args)
    {
        var parts = new string[args.Count];
        for (var i = 0; i < args.Count; i++)
            parts[i] = args[i].ToPrintString();

        AddLog(string.Join('\t', parts));
        return DynValue.NewNil();
    }

    private DynValue WaitCallback(ScriptExecutionContext context, CallbackArguments args)
    {
        var seconds = 0d;
        if (args.Count > 0)
        {
            if (args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("robot.wait expects a number of seconds");

            seconds = args[0].Number;
        }

        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;

        seconds = Math.Min(seconds, _options.MaxWait.TotalSeconds);
        return DynValue.NewYieldReq(new[] { DynValue.NewNumber(seconds) });
    }

    private void Fail(string message)
    {
        _lastError = message;
        _state = LuaProgramState.Error;
        _coroutine = null;
        _script = null;
        _robot = null;
        AddLog("[error] " + message);
    }

    private void AddLog(string message)
    {
        _log.Enqueue(message);
        while (_log.Count > Math.Max(1, _options.MaxLogLines))
            _log.Dequeue();

        _logVersion++;
        _logSink?.Invoke(message);
    }
}
