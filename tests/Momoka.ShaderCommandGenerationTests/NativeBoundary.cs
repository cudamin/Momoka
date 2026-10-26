using System.Collections.Concurrent;
using Momoka.Mpv;

namespace Momoka.Tests;

// Synthetic native endpoints only. A zero return is a fixture response, never evidence of mpv approval.
internal sealed class NativeBoundary
{
    internal static readonly ConcurrentDictionary<IntPtr, NativeBoundary> Contexts = new();
    private static long _nextContext = 5100;
    internal IntPtr Context { get; } = new(Interlocked.Increment(ref _nextContext));
    internal string File { get; set; } = "A";
    internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal)
    {
        ["glsl-shaders"] = @"C:\r051\old\;one.glsl;C:\r051\old-two.hook",
        ["scale"] = "bilinear",
        ["cscale"] = "bilinear",
        ["dscale"] = "bilinear",
        ["linear-downscaling"] = "no",
        ["correct-downscaling"] = "no",
        ["sigmoid-upscaling"] = "no",
        ["deband"] = "yes"
    };
    internal List<NativeCommand> Commands { get; } = [];
    internal List<string> Reads { get; } = [];
    internal List<string> PropertyWrites { get; } = [];
    internal Func<string[], int>? Result { get; set; }
    internal Func<string[], int>? LoadResult { get; set; }
    internal Action? Shutdown { get; set; }
    internal Action<string>? BeforeRead { get; set; }
    internal string? Missing { get; set; }
    internal int DestroyCalls { get; set; }
    internal int SetAttempts { get; set; }
    internal int ForbiddenCalls { get; set; }
    internal int NodeAllocations { get; set; }
    internal int NodeFrees { get; set; }

    internal NativeBoundary() => Contexts[Context] = this;

    internal int Command(string[] arguments)
    {
        if (arguments.Length == 0) throw new AssertionException("未安排空 native 命令");
        switch (arguments[0])
        {
            case "set" when arguments.Length == 3 && Values.ContainsKey(arguments[1]):
                SetAttempts++;
                var record = new NativeCommand(File, [.. arguments]);
                Commands.Add(record);
                var result = Result?.Invoke(arguments) ?? 0;
                record.Result = result;
                if (result >= 0) Values[arguments[1]] = arguments[2];
                return result;
            case "loadfile" when arguments.Length == 3 && arguments[2] == "replace"
                && new Uri(arguments[1]).Host == "r051.invalid":
                var load = new NativeCommand(File, [.. arguments]);
                Commands.Add(load);
                load.Result = LoadResult?.Invoke(arguments) ?? 0;
                if (load.Result >= 0) File = Values["force-media-title"];
                return load.Result.Value;
            case "quit" when arguments.Length == 1:
                Commands.Add(new NativeCommand(File, [.. arguments]));
                Shutdown?.Invoke();
                return 0;
            case "show-text" when arguments.Length is 2 or 3:
            case "seek" when arguments.Length == 3:
                Commands.Add(new NativeCommand(File, [.. arguments]));
                return Result?.Invoke(arguments) ?? 0;
            default:
                ForbiddenCalls++;
                throw new AssertionException("R051 拒绝未安排的 native 命令：" + arguments[0]);
        }
    }

    internal string? Read(string name)
    {
        Reads.Add(name);
        BeforeRead?.Invoke(name);
        if (name == Missing) return null;
        if (name == "profile-list") return "[]";
        if (name == "fullscreen") return "no";
        if (name == "vid") return "1";
        const string prefix = "option-info/";
        const string suffix = "/default-value";
        if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal))
        {
            var option = name[prefix.Length..^suffix.Length];
            return ShaderGroupCatalog.NeutralOptions.First(pair => pair.Key == option).Value;
        }
        return Values.GetValueOrDefault(name);
    }

    internal void Write(string name, string value)
    {
        if (!InlineSwitch.PerFilmNames.Contains(name, StringComparer.Ordinal)
            && !new[] { "start", "aid", "sid", "alang", "slang", "sub-files", "http-header-fields", "force-media-title", "pause" }
                .Contains(name, StringComparer.Ordinal))
        {
            ForbiddenCalls++;
            throw new AssertionException("R051 拒绝未安排的 native 属性写：" + name);
        }
        // Do not retain/log even the placeholder HTTP header value.
        PropertyWrites.Add(name);
        if (name != "http-header-fields") Values[name] = value;
    }

    internal IReadOnlyList<string>? ShaderList()
    {
        Reads.Add("glsl-shaders");
        if (Missing == "glsl-shaders") return null;
        var parts = new List<string>();
        var part = new System.Text.StringBuilder();
        var text = Values["glsl-shaders"];
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == ';') { part.Append(';'); i++; }
            else if (text[i] == ';') { parts.Add(part.ToString()); part.Clear(); }
            else part.Append(text[i]);
        }
        if (text.Length > 0) parts.Add(part.ToString());
        return parts;
    }
}

internal sealed class NativeCommand(string file, string[] arguments)
{
    internal string File { get; } = file;
    internal string[] Arguments { get; } = arguments;
    internal int? Result { get; set; }
    internal bool IsSet => Arguments[0] == "set";
}
