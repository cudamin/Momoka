namespace Momoka.SubtitleTranslationTests;

internal static class Program
{
    public static async Task<int> Main()
    {
        ConfigurationTests.Register();
        RequestTests.Register();
        SafetyTests.Register();
        CueTests.Register();
        return await Test.RunAsync();
    }
}

internal static class Test
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = new();
    public static void Case(string name, Action run) => Cases.Add((name, () => { run(); return Task.CompletedTask; }));
    public static void Async(string name, Func<Task> run) => Cases.Add((name, run));
    public static void True(bool condition) { if (!condition) throw new InvalidOperationException("断言失败。"); }
    public static void Equal<T>(T expected, T actual) => True(EqualityComparer<T>.Default.Equals(expected, actual));

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("未抛预期异常。");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("未抛预期异常。");
    }

    public static void Safe(Exception error)
    {
        True(error.InnerException is null);
        True(!error.ToString().Contains(Fixture.Key, StringComparison.Ordinal));
        True(!error.ToString().Contains(Fixture.BodyMarker, StringComparison.Ordinal));
    }

    public static async Task<int> RunAsync()
    {
        var passed = 0;
        foreach (var (name, run) in Cases)
        {
            try { await run(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error)
            {
                // 测试失败也不打印异常正文/请求/响应/密钥；仅显示用例名与异常类型。
                Console.WriteLine("FAIL " + name + " (" + error.GetType().Name + ")");
            }
        }
        Console.WriteLine($"SubtitleTranslation: {passed} passed, {Cases.Count - passed} failed, 0 skipped.");
        return passed == Cases.Count ? 0 : 1;
    }
}
