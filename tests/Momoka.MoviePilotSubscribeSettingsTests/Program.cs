using System.Text;

namespace Momoka.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("MoviePilot 搜索订阅设置：实际 Core/完整 VM/Toolkit/对话代码；匿名内存 HTTP；禁止账号、网络、窗口、播放");
        var cases = new List<(string Name, Func<Task> Run)>();
        SubscribeSettingsTests.Register(cases);
        var failed = 0;
        foreach (var (name, run) in cases)
        {
            try { await run(); Console.WriteLine("  通过 " + name); }
            catch (Exception error) { failed++; Console.WriteLine("  失败 " + name + "\n" + error); }
        }
        Console.WriteLine($"共 {cases.Count} 项，通过 {cases.Count - failed}，失败 {failed}，跳过 0");
        return failed == 0 ? 0 : 1;
    }
}

internal static class Check
{
    internal static void True(bool value, string message = "") { if (!value) throw new InvalidOperationException("期望为真：" + message); }
    internal static void False(bool value, string message = "") => True(!value, message);
    internal static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"期望 {expected}，实际 {actual}"); }
    internal static void Contains(string value, string text) => True(text.Contains(value, StringComparison.Ordinal), value + " 不在 " + text);
    internal static async Task ThrowsAsync<T>(Func<Task> run) where T : Exception
    {
        try { await run(); } catch (T) { return; }
        throw new InvalidOperationException("期望异常 " + typeof(T).Name);
    }
}
