using System.Reflection;
using System.Text;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ShaderBatchTests
{
    public static void Register()
    {
        Test("集成切链原生：一次配置加载、完整读回、临时文件清除", () => Apply().GetAwaiter().GetResult());
        Test("集成切链原生：解析部分失败即使返回零仍拒绝", () => Apply(partial: true).GetAwaiter().GetResult());
        Test("集成切链原生：旧文件代次拒绝整批写入", () => Stale().GetAwaiter().GetResult());
        Test("集成切链原生：取消入口零写入", () => Canceled().GetAwaiter().GetResult());
        Test("集成切链原生：写入排队期间换片后拒绝旧代次", () => Queued().GetAwaiter().GetResult());
        Test("集成切链原生：关闭恢复空链且缺读回不能确认成功", () => ClearAndMissing().GetAwaiter().GetResult());
        Test("集成切链配置：UTF8字节长度引用保留路径，拒绝注入", Configuration);
    }

    private static IShaderBatchPlayerControl Control(HandleFixture f) =>
        (object)f.Handle as IShaderBatchPlayerControl ?? throw new AssertionException("真实句柄缺少集成批量切链能力");

    private static IReadOnlyList<KeyValuePair<string, string>> Options() => ShaderSwitch.Options([], 0,
        ShaderGroupCatalog.Find("anime4k", GpuTier.Low), ShaderGroupCatalog.ShaderRoot);

    private static async Task Apply(bool partial = false)
    {
        await using var f = new HandleFixture(integrated: true);
        var control = Control(f);
        f.Native.Reads.Clear();
        Assert.True(control.SupportsShaderBatch);
        f.Native.PartialConfig = partial;
        var caller = Environment.CurrentManagedThreadId;
        Assert.Equal(!partial, await control.ApplyShaderOptionsForFileAsync(control.FileGeneration, Options(), CancellationToken.None));
        Assert.Equal(1, f.Native.ConfigFiles.Count);
        Assert.Equal(0, f.Native.SetAttempts, "批量路径不发逐项set");
        Assert.False(f.Native.ConfigThread == caller);
        Assert.False(File.Exists(f.Native.ConfigFiles[0]), "临时配置必须在完成后删除");
        Assert.Equal(8, f.Native.Reads.Count, "每个选项均从native读回，不能仅信返回码");
        if (!partial)
            foreach (var (name, value) in Options()) Assert.Equal(value, f.Native.Values[name], name);
    }

    private static async Task Stale()
    {
        await using var f = new HandleFixture(integrated: true);
        var control = Control(f);
        var generation = control.FileGeneration;
        f.Handle.HandOver();
        f.Native.Reads.Clear();
        Assert.False(await control.ApplyShaderOptionsForFileAsync(generation, Options(), CancellationToken.None));
        Assert.Equal(0, f.Native.ConfigFiles.Count);
        Assert.Equal(0, f.Native.Reads.Count);
    }

    private static async Task Canceled()
    {
        await using var f = new HandleFixture(integrated: true);
        var control = Control(f);
        try
        {
            await control.ApplyShaderOptionsForFileAsync(control.FileGeneration, Options(), new CancellationToken(true));
            throw new AssertionException("入口取消必须传播");
        }
        catch (OperationCanceledException) { }
        Assert.Equal(0, f.Native.ConfigFiles.Count);
    }

    private static async Task Queued()
    {
        await using var f = new HandleFixture(integrated: true);
        var control = Control(f);
        Task<bool>? pending = null;
        f.Gate.Run(() =>
        {
            pending = control.ApplyShaderOptionsForFileAsync(control.FileGeneration, Options(), CancellationToken.None);
            f.Handle.HandOver();
            return true;
        });
        Assert.False(await pending!);
        Assert.Equal(0, f.Native.ConfigFiles.Count);
    }

    private static async Task ClearAndMissing()
    {
        await using var f = new HandleFixture(integrated: true);
        var control = Control(f);
        Assert.True(await control.ApplyShaderOptionsForFileAsync(control.FileGeneration, Options(), CancellationToken.None));
        Assert.True(await control.ApplyShaderOptionsForFileAsync(control.FileGeneration,
            ShaderSwitch.Options([], 0, null, ShaderGroupCatalog.ShaderRoot), CancellationToken.None));
        Assert.Equal("", f.Native.Values["glsl-shaders"]);
        f.Native.Missing = "glsl-shaders";
        Assert.False(await control.ApplyShaderOptionsForFileAsync(control.FileGeneration, Options(), CancellationToken.None));
    }

    private static void Configuration()
    {
        var method = typeof(ShaderSwitch).GetMethod("Configuration", BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertionException("缺少受控配置编码");
        var options = Options().ToArray();
        var value = @"C:\中文 # % ' ""\a\;b.glsl";
        options[^1] = new("glsl-shaders", value);
        var result = (string)method.Invoke(null, [options])!;
        Assert.Contains($"glsl-shaders=%{Encoding.UTF8.GetByteCount(value)}%{value}\n", result);
        foreach (var invalid in new[] { "\ninclude=bad.conf", "\rprofile=bad", "\0" })
        {
            options[^1] = new("glsl-shaders", invalid);
            try { method.Invoke(null, [options]); throw new AssertionException("必须拒绝配置注入"); }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { }
        }
        options[^1] = new("include", "bad.conf");
        try { method.Invoke(null, [options]); throw new AssertionException("必须拒绝非画质选项"); }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { }
    }
}
