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
        InstallGluaUtilities(script);
        return script;
    }

    private const string GluaUtilitySource = """
        do
            function istable(value)
                return type(value) == "table"
            end

            function isstring(value)
                return type(value) == "string"
            end

            function isnumber(value)
                return type(value) == "number"
            end

            function isbool(value)
                return type(value) == "boolean"
            end

            function isfunction(value)
                return type(value) == "function"
            end

            local function contains(tbl, value)
                if type(tbl) ~= "table" then
                    return false
                end

                for _, entry in pairs(tbl) do
                    if entry == value then
                        return true
                    end
                end

                return false
            end

            table.HasValue = contains
            table.Contains = contains

            function table.IsEmpty(tbl)
                if type(tbl) ~= "table" then
                    return true
                end

                return next(tbl) == nil
            end

            function table.Count(tbl)
                if type(tbl) ~= "table" then
                    return 0
                end

                local count = 0
                for _ in pairs(tbl) do
                    count = count + 1
                end

                return count
            end

            function table.GetKeys(tbl)
                local keys = {}
                if type(tbl) ~= "table" then
                    return keys
                end

                for key in pairs(tbl) do
                    keys[#keys + 1] = key
                end

                return keys
            end

            function table.KeyFromValue(tbl, value)
                if type(tbl) ~= "table" then
                    return nil
                end

                for key, entry in pairs(tbl) do
                    if entry == value then
                        return key
                    end
                end

                return nil
            end

            function table.Random(tbl)
                if type(tbl) ~= "table" then
                    return nil
                end

                local count = 0
                for _ in pairs(tbl) do
                    count = count + 1
                end

                if count == 0 then
                    return nil
                end

                local wanted = math.random(count)
                local index = 0
                for key, value in pairs(tbl) do
                    index = index + 1
                    if index == wanted then
                        return value, key
                    end
                end

                return nil
            end

            function table.Add(target, source)
                if type(target) ~= "table" or type(source) ~= "table" or target == source then
                    return target
                end

                for index = 1, #source do
                    target[#target + 1] = source[index]
                end

                return target
            end

            function table.RemoveByValue(tbl, value)
                if type(tbl) ~= "table" then
                    return false
                end

                for index = 1, #tbl do
                    if tbl[index] == value then
                        table.remove(tbl, index)
                        return index
                    end
                end

                return false
            end

            function string.Trim(input, char)
                local pattern = char or "%s"
                return (tostring(input):gsub("^" .. pattern .. "+", ""):gsub(pattern .. "+$", ""))
            end

            function string.StartsWith(input, start)
                input = tostring(input)
                start = tostring(start)
                return start == "" or input:sub(1, #start) == start
            end

            function string.StartWith(input, start)
                return string.StartsWith(input, start)
            end

            function string.EndsWith(input, ending)
                input = tostring(input)
                ending = tostring(ending)
                return ending == "" or input:sub(-#ending) == ending
            end

            function string.Split(input, separator)
                local result = {}
                input = tostring(input)
                separator = tostring(separator or ",")
                if separator == "" then
                    result[1] = input
                    return result
                end

                local start = 1
                while true do
                    local first, last = string.find(input, separator, start, true)
                    if not first then
                        result[#result + 1] = string.sub(input, start)
                        break
                    end

                    result[#result + 1] = string.sub(input, start, first - 1)
                    start = last + 1
                end

                return result
            end
        end
        """;

    private static void InstallGluaUtilities(Script script)
    {
        var chunk = script.LoadString(GluaUtilitySource, null, "glua-utils");
        if (chunk == null)
            return;

        script.Call(chunk);
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
