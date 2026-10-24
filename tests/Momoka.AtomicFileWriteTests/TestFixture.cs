using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Infrastructure;

namespace Momoka.AtomicFileWriteTests;

internal sealed record TestCase(string Id, string Requirement, Action<TestFixture> Run);

internal sealed class TestFixture(string root, Action<string> log)
{
    private readonly Dictionary<string, object?> _facts = [];

    public string Root { get; } = Path.GetFullPath(root);
    public IReadOnlyDictionary<string, object?> Facts => _facts;
    public string FilePath(string name) => Path.Combine(Root, name);

    public string Subdirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Fact(string name, object? value)
    {
        _facts[name] = value;
        log($"    {name}={JsonSerializer.Serialize(value)}");
    }

    public void Snapshot(string label, string path) => Fact(label, FileState.Capture(path));

    public void AtOperation(string scenario, int target, int operation, string kind)
    {
        // Called by the serial coordinator only. Concurrent workers report their first failure
        // through FirstFailure rather than racing this dictionary or inferring commit order.
        _facts["lastOperation"] = new { scenario, target, operation, kind };
    }
}

internal static class ProductionBody
{
    private static readonly Action<string, Action<string>, string?> Write = Bind();

    private static Action<string, Action<string>, string?> Bind()
    {
        // Fault injection into the ACTUAL production Write body. Public-path cases remain public.
        // No implementation is copied; a changed/missing signature fails closed.
        var method = typeof(AtomicFile).GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(string), typeof(Action<string>), typeof(string)], modifiers: null)
            ?? throw new ContractFailure("当前生产 AtomicFile.Write 故障注入边界不存在");
        return method.CreateDelegate<Action<string, Action<string>, string?>>();
    }

    public static void Invoke(string path, Action<string> body, string? backup) => Write(path, body, backup);
}

internal static class Documents
{
    public static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Encoded(string text, Encoding encoding) =>
        [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    public static string Text(int version, int bodyLength, uint salt = 0)
    {
        var body = new string((char)('a' + (version % 26 + 26) % 26), bodyLength);
        return JsonSerializer.Serialize(new
        {
            fixture = "anonymous-atomic-contract",
            version,
            salt,
            body,
            unicode = "匿名-é-Ω-文件-🎬",
            end = $"complete-{version}-{salt:X8}"
        });
    }
}

internal sealed class InjectedBodyFailure : Exception { }

internal sealed class FirstFailure
{
    private Exception? _error;
    public Exception? Error => Volatile.Read(ref _error);
    public bool Set(Exception error) => Interlocked.CompareExchange(ref _error, error, null) is null;
}
