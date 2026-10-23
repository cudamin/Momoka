using Momoka.FnNas;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

internal static class Program
{
    internal static ShellRuntime Runtime { get; private set; } = null!;
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is not [var shell, var root, var proof])
        {
            Console.Error.WriteLine("用法：Momoka.DetailDeleteCommandTests <本树 fresh Release Momoka.dll> <本树根目录> <成功构建证明 JSON>");
            return 2;
        }
        try
        {
            Runtime = new ShellRuntime(shell, root, proof);
            DeleteCommandTests.Register();
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

internal sealed class ShellRuntime
{
    internal const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Assembly _assembly;
    public string Root { get; }
    public ShellRuntime(string shell, string root, string proof)
    {
        Root = Path.GetFullPath(root);
        var expected = Path.Combine(Root, "src", "Momoka.Shell", "bin", "x64", "Release",
            "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        Assert.Equal(expected, Path.GetFullPath(shell), "拒绝主线程/外树/旧发布 DLL");
        using var document = JsonDocument.Parse(File.ReadAllText(proof));
        var evidence = document.RootElement;
        Assert.Equal(expected, evidence.GetProperty("ShellPath").GetString());
        Assert.Equal(Hash(expected), evidence.GetProperty("ShellSha256").GetString());
        Assert.Equal(0, evidence.GetProperty("BuildExit").GetInt32(), "必须成功编译后再跑");
        if (evidence.TryGetProperty("FixtureSha256", out var fixtureHash))
            Assert.Equal(Hash(typeof(Program).Assembly.Location), fixtureHash.GetString(), "夹具改后必须重编译");
        foreach (var source in evidence.GetProperty("Sources").EnumerateArray())
        {
            var relative = source.GetProperty("Path").GetString()!;
            Assert.True(relative is "src/Momoka.Shell/ViewModels/DetailViewModel.cs"
                or "src/Momoka.Shell/Views/DetailPage.xaml.cs" or "src/Momoka.Shell/Views/ItemCommands.cs"
                or "src/Momoka.Shell/Views/ItemCommands.FnNas.cs", "构建证明仅题内四源");
            Assert.Equal(Hash(Path.Combine(Root, relative)), source.GetProperty("Sha256").GetString(), "源改后必须重编译");
            Console.WriteLine($"SOURCE {relative} {source.GetProperty("Sha256").GetString()}");
        }
        Assert.Equal(4, evidence.GetProperty("Sources").GetArrayLength());
        var directory = Path.GetDirectoryName(expected)!;
        Assert.Equal(Hash(typeof(EmbyClient).Assembly.Location), Hash(Path.Combine(directory, "Momoka.Core.dll")),
            "命令与真VM必须使用相同 Core");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        _assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(expected);
        Console.WriteLine($"SHELL fresh={expected} sha256={Hash(expected)} mvid={_assembly.ManifestModule.ModuleVersionId}");
        Console.WriteLine("实际 ItemCommands/FnNas/Guard/Ask/Tell/Confirm + EmbyClient/FnNasClient DELETE + 真 detail VM；无 App、UI/native、真实服务器、凭据或文件删除");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public Type Type(string name) => _assembly.GetType(name, throwOnError: true)!;
    public object New(string name) => Activator.CreateInstance(Type(name), nonPublic: true)!;
    public object Card(EmbyItem item, EmbyImageStore images)
    {
        var constructor = Type("Momoka.Shell.Views.CardItem").GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "item" => (object)item,
            "images" => images,
            "width" => 170,
            _ => parameter.DefaultValue
        }).ToArray());
    }
    public static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, Members) ?? throw new AssertionException("实际VM缺少入口 " + name);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    public static object? Get(object target, string name) => target.GetType().GetProperty(name, Members)!.GetValue(target);
}

/// <summary>Forwarding adapter only. No delete/reload/state decision is implemented here.</summary>
internal sealed class DetailVmBridge(object vm, EmbyImageStore images)
{
    public object Vm { get; } = vm;
    public EmbyItem? CurrentItem => ShellRuntime.Get(Vm, "CurrentItem") as EmbyItem;
    public IReadOnlyList<EmbyItem> Episodes => (IReadOnlyList<EmbyItem>)ShellRuntime.Get(Vm, "Episodes")!;
    public void Reload() => ShellRuntime.Invoke(Vm, "Reload");
    public Action CaptureReload() => Vm.GetType().GetMethod("CaptureReload", ShellRuntime.Members) is { } factory
        ? (Action)factory.Invoke(Vm, null)! : Reload;
    public Action<CardItem> CaptureRemoval()
    {
        var method = Vm.GetType().GetMethod("CaptureRemoval", ShellRuntime.Members);
        if (method is null)
            return card => ShellRuntime.Invoke(Vm, "Remove", Program.Runtime.Card(card.Item, images));
        var callback = (Delegate)method.Invoke(Vm, null)!;
        return card => callback.DynamicInvoke(Program.Runtime.Card(card.Item, images));
    }
}

internal sealed class FixtureContext : SynchronizationContext, IDisposable
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly AutoResetEvent _ready = new(false);
    private readonly SynchronizationContext? _previous = Current;
    public FixtureContext() => SetSynchronizationContext(this);
    public override void Post(SendOrPostCallback callback, object? state)
    {
        _queue.Enqueue((callback, state));
        _ready.Set();
    }
    public void Drain()
    {
        while (_queue.TryDequeue(out var entry)) entry.Callback(entry.State);
    }
    public void Until(Func<bool> predicate)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!predicate())
        {
            Drain();
            if (Environment.TickCount64 >= deadline) throw new AssertionException("实际命令/VM超过10秒仍未达到状态");
            if (!predicate()) _ready.WaitOne(1);
        }
        Drain();
    }
    public void Await(Task task)
    {
        Until(() => task.IsCompleted);
        if (task.IsFaulted) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(task.Exception!.InnerException!).Throw();
        if (task.IsCanceled) throw new AssertionException("夹具顶层任务意外取消");
    }
    public void Dispose()
    {
        SetSynchronizationContext(_previous);
        _ready.Dispose();
    }
}

internal sealed class DialogPlan
{
    public static DialogPlan Current { get; set; } = null!;
    public ContentDialogResult VersionResult { get; set; } = ContentDialogResult.Primary;
    public ContentDialogResult ConfirmResult { get; set; } = ContentDialogResult.Primary;
    public int? Selection { get; set; } = 0;
    public bool FailConfirm { get; set; }
    public Action? BeforeConfirm { get; set; }
    public List<FnNasVersion> Versions { get; } = [];
    public List<ContentDialog> Confirmations { get; } = [];
    public int VersionDialogs { get; private set; }
    public Task<ContentDialogResult> Show(ContentDialog dialog)
    {
        if (dialog is FnNasVersionsDialog versions)
        {
            VersionDialogs++;
            Versions.AddRange(versions.Versions);
            if (Selection is { } index) versions.Selected = versions.Versions[index];
            return Task.FromResult(VersionResult);
        }
        Assert.Equal(ContentDialogButton.Close, dialog.DefaultButton, "实际确认默认必须取消");
        Assert.Equal("取消", dialog.CloseButtonText);
        Assert.Equal("删除", dialog.PrimaryButtonText);
        Confirmations.Add(dialog);
        BeforeConfirm?.Invoke();
        if (FailConfirm) throw new InvalidOperationException("anonymous dialog busy");
        return Task.FromResult(ConfirmResult);
    }
}
