using System;
using MoonSharp.Interpreter;

namespace Content.Server._PS.Circuitry.Lua;

/// <summary>
/// Builds the MoonSharp environment used for player-authored circuitry programs.
/// </summary>
/// <remarks>
/// The interpreter is deliberately limited to the standard library pieces a program
/// needs for arithmetic, strings and tables. Files, modules, process access, debug
/// introspection and dynamic code loading are not exposed. Allocation-heavy string
/// helpers are wrapped with size limits; the runtime also has per-resume budgets.
/// </remarks>
internal static class LuaSandbox
{
    private static readonly string[] ForbiddenGlobals =
    {
        "load",
        "loadfile",
        "dofile",
        "require",
        "package",
        "io",
        "os",
        "debug",
    };

    public static Script CreateScript(LuaProgramOptions options, Action<string> logSink)
    {
        var script = new Script(
            CoreModules.Preset_HardSandbox |
            CoreModules.ErrorHandling |
            CoreModules.Metatables);

        script.Options.DebugPrint = logSink;
        script.Options.CheckThreadAccess = true;

        foreach (var name in ForbiddenGlobals)
            script.Globals.Set(name, DynValue.NewNil());

        // collectgarbage is part of MoonSharp's Basic module but gives scripts a
        // cheap way to interfere with server GC behaviour.
        script.Globals.Set("collectgarbage", DynValue.NewNil());

        InstallStringGuards(script, options);
        return script;
    }

    private static void InstallStringGuards(Script script, LuaProgramOptions options)
    {
        var stringValue = script.Globals.Get("string");
        if (stringValue.Type != DataType.Table)
            return;

        var stringTable = stringValue.Table;
        var originalRep = stringTable.Get("rep");
        var originalFormat = stringTable.Get("format");

        // String.dump produces bytecode. There is no load() to consume it, but
        // removing it keeps the sandbox surface smaller.
        stringTable.Set("dump", DynValue.NewNil());

        if (originalRep.Type is DataType.Function or DataType.ClrFunction)
        {
            stringTable.Set("rep", DynValue.NewCallback((context, args) =>
            {
                if (args.Count < 2 ||
                    args[0].Type != DataType.String ||
                    args[1].Type != DataType.Number)
                {
                    return script.Call(originalRep, args.GetArray(0));
                }

                var count = args[1].Number;
                if (double.IsNaN(count) || double.IsInfinity(count) || count < 0 || count > int.MaxValue)
                    throw new ScriptRuntimeException("string.rep count is too large");

                var separator = args.Count > 2 && args[2].Type == DataType.String
                    ? args[2].String
                    : string.Empty;

                var length = (long) args[0].String.Length * (long) count +
                             (long) separator.Length * Math.Max(0, (long) count - 1);

                if (length > options.MaxStringBytes)
                    throw new ScriptRuntimeException("string.rep result is too large");

                return script.Call(originalRep, args.GetArray(0));
            }, "rep"));
        }

        if (originalFormat.Type is DataType.Function or DataType.ClrFunction)
        {
            stringTable.Set("format", DynValue.NewCallback((context, args) =>
            {
                if (args.Count == 0 || args[0].Type != DataType.String)
                    return script.Call(originalFormat, args.GetArray(0));

                var format = args[0].String;
                if (format.Length > options.MaxStringBytes ||
                    HasOversizedFormatSpecifier(format, options.MaxStringBytes))
                {
                    throw new ScriptRuntimeException("string.format is too large");
                }

                var result = script.Call(originalFormat, args.GetArray(0));
                if (result.Type == DataType.String && result.String.Length > options.MaxStringBytes)
                    throw new ScriptRuntimeException("string.format result is too large");

                return result;
            }, "format"));
        }
    }

    /// <summary>
    /// Rejects width/precision values large enough to make string.format allocate
    /// excessive memory. Dynamic-width '*' specifiers are also rejected.
    /// </summary>
    private static bool HasOversizedFormatSpecifier(string format, int maxLength)
    {
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
                continue;

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                i++;
                continue;
            }

            var j = i + 1;
            while (j < format.Length && "-+ #0".IndexOf(format[j]) >= 0)
                j++;

            if (j < format.Length && format[j] == '*')
                return true;

            var start = j;
            while (j < format.Length && char.IsDigit(format[j]))
                j++;

            if (j > start)
            {
                if (!long.TryParse(format.AsSpan(start, j - start), out var width) || width > maxLength)
                    return true;
            }

            if (j < format.Length && format[j] == '.')
            {
                j++;
                if (j < format.Length && format[j] == '*')
                    return true;

                start = j;
                while (j < format.Length && char.IsDigit(format[j]))
                    j++;

                if (j > start &&
                    (!long.TryParse(format.AsSpan(start, j - start), out var precision) || precision > maxLength))
                {
                    return true;
                }
            }

            i = j;
        }

        return false;
    }
}
