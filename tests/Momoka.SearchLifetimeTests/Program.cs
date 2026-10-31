using System.Text;

namespace Momoka.Tests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (HandleArguments(args, Console.Out, Console.Error) is { } argumentExit) return argumentExit;
        Console.WriteLine("PERF-01 / PERF-12：完整生产 VM/页面/对话代码、Toolkit 与真实 Core；匿名内存 HTTP；不运行真实 WinUI/库/播放/凭据");
        var cases = new List<(string Name, Func<Task> Run)>();
        MoviePilotLifetimeTests.Register(cases);
        SubscriptionNavigationTests.Register(cases);
        FnNasLifetimeTests.Register(cases);
        RunnerContractTests.Register(cases);
        var failed = 0;
        foreach (var (name, run) in cases)
        {
            try { await run().WaitAsync(TimeSpan.FromSeconds(10)); Console.WriteLine("  通过 " + name); }
            catch (Exception error) { failed++; Console.WriteLine("  失败 " + name + "\n" + error); }
            finally
            {
                Microsoft.UI.Dispatching.DispatcherQueue.Pending.Clear();
                Microsoft.UI.Xaml.Controls.ContentDialog.ShowEndpoint = null;
            }
        }
        Console.WriteLine($"共 {cases.Count} 项，通过 {cases.Count - failed}，失败 {failed}，跳过 0");
        return failed == 0 ? 0 : 1;
    }

    // Reject all unknown modes before any fixture, Core service, IO or test registration is created.
    internal static int? HandleArguments(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0) return null;
        if (args is ["--help"])
        {
            output.WriteLine("Momoka.SearchLifetimeTests：无参数运行全部离线定向回归；--help 只显示帮助。其他参数退出码 2，不运行夹具。");
            return 0;
        }
        error.WriteLine("参数无效：仅接受无参数或单独 --help；不接受路径、URL、账号、token、UI 或播放模式。");
        return 2;
    }
}

internal static class Check
{
    internal static void True(bool value, string message = "") { if (!value) throw new InvalidOperationException("期望为真：" + message); }
    internal static void False(bool value, string message = "") => True(!value, message);
    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
    }
    internal static void Same(object? expected, object? actual) => True(ReferenceEquals(expected, actual), "必须是同一个对象");
    internal static void Null(object? value) => True(value is null, "必须为空");
    internal static void Contains(string value, string text) => True(text.Contains(value, StringComparison.Ordinal), value + " 不在 " + text);
    internal static async Task Until(Func<bool> predicate)
    {
        for (var i = 0; i < 1000; i++) { if (predicate()) return; await Task.Delay(1); }
        throw new InvalidOperationException("等待受控条件超时");
    }
}
