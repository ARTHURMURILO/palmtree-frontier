#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Content.Server._PS.Circuitry.Lua;
using MoonSharp.Interpreter;
using NUnit.Framework;

namespace Content.Tests._PS.Circuitry;

[TestFixture]
public sealed class LuaProgramTest
{
    private static LuaProgramOptions Options() => new()
    {
        AutoYieldInstructions = 1_000,
        TickBudget = TimeSpan.FromMilliseconds(10),
        MaxNoWaitTime = TimeSpan.FromSeconds(1),
        MaxLogLines = 100,
    };

    private static LuaProgram Start(
        string source,
        out List<string> log,
        LuaProgramOptions? options = null,
        LuaApiRegistrar? api = null)
    {
        log = new List<string>();
        var program = new LuaProgram(source, options ?? Options(), log.Add, api);
        Assert.That(program.Start(TimeSpan.Zero), Is.True, program.LastError);
        return program;
    }

    [Test]
    public void SandboxBlocksDangerousGlobals()
    {
        var program = Start(
            "function main() log(tostring(os), tostring(io), tostring(require), tostring(load), " +
            "tostring(debug), tostring(package), tostring(collectgarbage)) end",
            out var log);

        // A single Update can yield before finishing on a loaded CI machine; keep advancing the
        // program until it settles rather than assuming one tick is enough.
        for (var i = 0;
             i < 100 && program.State is LuaProgramState.Loading or LuaProgramState.Running or LuaProgramState.Waiting;
             i++)
        {
            program.Update(TimeSpan.Zero);
        }

        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Finished), program.LastError);
            Assert.That(log, Has.Count.EqualTo(1));
            Assert.That(log[0], Is.EqualTo("nil\tnil\tnil\tnil\tnil\tnil\tnil"));
        });
    }

    [Test]
    public void WaitReturnsElapsedTime()
    {
        var program = Start(
            "function main() log('start'); local dt = robot.wait(1.0); log('end', tostring(dt)) end",
            out var log);

        program.Update(TimeSpan.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Waiting), program.LastError);
            Assert.That(log, Is.EqualTo(new[] { "start" }));
        });

        program.Update(TimeSpan.FromSeconds(0.5));
        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Waiting), program.LastError);
            Assert.That(log, Has.Count.EqualTo(1));
        });

        program.Update(TimeSpan.FromSeconds(1.0));
        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Finished), program.LastError);
            Assert.That(log, Has.Count.EqualTo(2));
            Assert.That(log[1], Does.StartWith("end\t"));

            var elapsed = double.Parse(log[1].Split('\t')[1], CultureInfo.InvariantCulture);
            Assert.That(elapsed, Is.EqualTo(1.0).Within(0.01));
        });
    }

    [Test]
    public void InfiniteLoopIsPreemptedAndStopped()
    {
        var options = new LuaProgramOptions
        {
            AutoYieldInstructions = 100,
            TickBudget = TimeSpan.FromMilliseconds(1),
            MaxNoWaitTime = TimeSpan.FromSeconds(0.5),
        };

        var program = Start(
            "function main() while true do local x = 1 + 1 end end",
            out _,
            options);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 20 && program.IsRunning; i++)
            program.Update(TimeSpan.FromSeconds(i * 0.1));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Error), program.LastError);
            Assert.That(program.LastError, Does.Contain("budget"));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void LargeProgramWithinLimitRuns()
    {
        var filler = new string(' ', 128 * 1024);
        var program = new LuaProgram(
            "--" + filler + "\nfunction main() log('large ok') end",
            new LuaProgramOptions
            {
                MaxSourceBytes = 256 * 1024,
                AutoYieldInstructions = 1_000,
                TickBudget = TimeSpan.FromMilliseconds(10),
            });

        Assert.That(program.Start(TimeSpan.Zero), Is.True, program.LastError);
        program.Update(TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Finished), program.LastError);
            Assert.That(program.Log, Does.Contain("large ok"));
        });
    }

    [Test]
    public void SourceSizeIsCapped()
    {
        var program = new LuaProgram(
            new string('a', 64),
            new LuaProgramOptions { MaxSourceBytes = 16 });

        Assert.Multiple(() =>
        {
            Assert.That(program.Start(TimeSpan.Zero), Is.False);
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Error));
            Assert.That(program.LastError, Does.Contain("byte limit"));
        });
    }

    [Test]
    public void OversizedStringRepIsRejected()
    {
        var options = new LuaProgramOptions
        {
            AutoYieldInstructions = 1_000,
            TickBudget = TimeSpan.FromMilliseconds(10),
            MaxStringBytes = 1024,
        };

        var program = Start(
            "function main() local s = string.rep('x', 1000000000) end",
            out _,
            options);

        program.Update(TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Error), program.LastError);
            Assert.That(program.LastError, Does.Contain("too large"));
        });
    }

    [Test]
    public void SyntaxErrorsAreReported()
    {
        var program = new LuaProgram("function main( end");

        Assert.Multiple(() =>
        {
            Assert.That(program.Start(TimeSpan.Zero), Is.False);
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Error));
            Assert.That(program.LastError, Is.Not.Empty);
        });
    }

    [Test]
    public void LongRunningWaitLoopStaysHealthyAndLogIsCapped()
    {
        var options = new LuaProgramOptions
        {
            AutoYieldInstructions = 1_000,
            TickBudget = TimeSpan.FromMilliseconds(10),
            MaxNoWaitTime = TimeSpan.FromSeconds(1),
            MaxLogLines = 10,
        };

        var program = Start(
            "function main() while true do robot.wait(0.1) log('tick') end end",
            out var log,
            options);

        for (var i = 0; i < 500; i++)
            program.Update(TimeSpan.FromSeconds(i * 0.1));

        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Waiting), program.LastError);
            Assert.That(program.LastError, Is.Null);
            Assert.That(program.Log, Has.Count.EqualTo(10));
        });
    }

    [Test]
    public void LogFloodIsCapped()
    {
        var options = new LuaProgramOptions
        {
            AutoYieldInstructions = 1_000,
            TickBudget = TimeSpan.FromMilliseconds(10),
            MaxNoWaitTime = TimeSpan.FromSeconds(1),
            MaxLogLines = 5,
        };

        var program = Start(
            "function main() for i = 1, 10000 do log(i) end end",
            out var log,
            options);

        for (var i = 0; i < 100 && program.State is LuaProgramState.Loading or LuaProgramState.Running; i++)
            program.Update(TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Finished), program.LastError);
            Assert.That(program.Log, Has.Count.EqualTo(5));
            Assert.That(program.Log[^1], Is.EqualTo("10000"));
        });
    }

    [Test]
    public void PerceptionHookCanProcessSpeech()
    {
        var program = Start(
            """
            function main()
                local ev = robot.hear()
                if ev and ev.message:lower():find("help", 1, true) then
                    log("heard", ev.name, ev.job, ev.message)
                end
            end
            """,
            out var log,
            api: (script, robot) =>
            {
                robot.Set("hear", DynValue.NewCallback((context, args) =>
                {
                    var ev = new Table(script);
                    ev.Set("message", DynValue.NewString("HELP me"));
                    ev.Set("name", DynValue.NewString("Alice"));
                    ev.Set("job", DynValue.NewString("Engineer"));
                    return DynValue.NewTable(ev);
                }, "hear"));
            });

        program.Update(TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(program.State, Is.EqualTo(LuaProgramState.Finished), program.LastError);
            Assert.That(log, Has.Count.EqualTo(1));
            Assert.That(log[0], Is.EqualTo("heard\tAlice\tEngineer\tHELP me"));
        });
    }
}
