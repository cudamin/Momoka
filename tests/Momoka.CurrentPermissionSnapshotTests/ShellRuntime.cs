using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>Load the current in-tree Shell assembly, but never call an App/Window/entry point.</summary>
internal sealed class PermissionShellRuntime
{
    private readonly Assembly _shell;
    internal PermissionShellRuntime(string assemblyPath)
    {
        var expected = Path.Combine(Program.Root, "src", "Momoka.Shell", "bin", "x64", "Release",
            "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        Assert.Equal(expected, Path.GetFullPath(assemblyPath), "只用此树当前 Release Shell");
        Assert.True(File.Exists(expected), "必须先成功编译当前 Shell");
        var directory = Path.GetDirectoryName(expected)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        _shell = AssemblyLoadContext.Default.LoadFromAssemblyPath(expected);
    }

    internal Type Type(string name) => _shell.GetType("Momoka.Shell.ViewModels." + name, throwOnError: true)!;
    internal object New(string name, params object?[] args) => Activator.CreateInstance(Type(name), PermissionFixture.Members, null, args, null)!;
    internal object? Call(object target, string name, params object?[] args) => PermissionFixture.Invoke(target, name, args);
    internal object? Get(object target, string name) => target.GetType().GetProperty(name, PermissionFixture.Members)!.GetValue(target);
    internal T Get<T>(object target, string name) => (T)Get(target, name)!;
    internal void Set(object target, string name, object? value) => target.GetType().GetProperty(name, PermissionFixture.Members)!.SetValue(target, value);

    internal SettingsVmProxy Settings(IServerCapabilities capabilities)
    {
        var vm = New("SettingsViewModel");
        var method = Type("SettingsViewModel").GetMethod("Attach", PermissionFixture.Members)!;
        var memory = new PermissionMemorySettings();
        var arguments = method.GetParameters().Select(parameter => parameter.Name switch
        {
            "settings" => (object)memory,
            "paths" => new AppPaths(Path.Combine(Program.Root, "artifacts", "r011-current-permission", "forbidden-vm|no-IO")),
            "capabilities" => capabilities,
            _ => null
        }).ToArray();
        method.Invoke(vm, arguments);
        return new SettingsVmProxy(this, vm, memory);
    }

    internal void AddDraft(object vm)
    {
        var row = New("SettingTextRow", "未完成输入", "匿名草稿", "", "pending-draft", (Func<string, string>)(value => value), (Action)(() => { }));
        var rows = Array.CreateInstance(Type("SettingRow"), 1);
        rows.SetValue(row, 0);
        var section = New("SettingSection", "主页", "草稿卡", "不写入", rows);
        ((IList)Get(vm, "Sections")!).Add(section);
        Set(vm, "IsReady", true);
    }

    internal void VerifyPageHash()
    {
        var page = Path.Combine(Program.Root, "src", "Momoka.Shell", "Views", "SettingsPage.xaml.cs");
        Assert.Equal(Momoka.Shell.Views.PermissionPage.ProductionPageSha256,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(page))), "执行的页面方法须来自当前文件");
    }
}

internal sealed class SettingsVmProxy : IDisposable, INotifyPropertyChanged
{
    private readonly PermissionShellRuntime _runtime;
    internal object Actual { get; }
    internal PermissionMemorySettings Memory { get; }
    internal SettingsVmProxy(PermissionShellRuntime runtime, object vm, PermissionMemorySettings memory)
    {
        _runtime = runtime;
        Actual = vm;
        Memory = memory;
    }
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => ((INotifyPropertyChanged)Actual).PropertyChanged += value;
        remove => ((INotifyPropertyChanged)Actual).PropertyChanged -= value;
    }
    internal string SelectedCategory { get => _runtime.Get<string>(Actual, nameof(SelectedCategory)); set => _runtime.Set(Actual, nameof(SelectedCategory), value); }
    internal string? SelectedSourceKey => _runtime.Get<string?>(Actual, nameof(SelectedSourceKey));
    internal bool ShowsHosted => _runtime.Get<bool>(Actual, nameof(ShowsHosted));
    internal bool Hidden => _runtime.Get<bool>(Actual, "ServerAdministrationHidden");
    internal object CategoryGroups => _runtime.Get(Actual, nameof(CategoryGroups))!;
    internal string[] Labels => ((IEnumerable)_runtime.Get(Actual, "Categories")!).Cast<object>().Select(entry => _runtime.Get<string>(entry, "Label")).ToArray();
    internal string[] LabelsFor(string sourceKey) => ((IEnumerable)_runtime.Get(Actual, "Categories")!).Cast<object>()
        .Where(entry => _runtime.Get<string?>(entry, "SourceKey") == sourceKey)
        .Select(entry => _runtime.Get<string>(entry, "Label")).ToArray();
    internal string[] GroupNames => ((IEnumerable)CategoryGroups).Cast<object>().Select(group => _runtime.Get<string>(group, "Name")).ToArray();
    internal string Key(string label, string? sourceKey = null) => ((IEnumerable)_runtime.Get(Actual, "Categories")!).Cast<object>()
        .Where(entry => _runtime.Get<string>(entry, "Label") == label && _runtime.Get<string?>(entry, "SourceKey") == sourceKey)
        .Select(entry => _runtime.Get<string>(entry, "Key")).Single();
    internal Task RefreshServerAdministrationAsync() => (Task)_runtime.Call(Actual, nameof(RefreshServerAdministrationAsync))!;
    internal bool CanOpenServerCategory(string value) => (bool)_runtime.Call(Actual, nameof(CanOpenServerCategory), value)!;
    internal void Activate() => _runtime.Call(Actual, nameof(Activate));
    internal void Cancel() => _runtime.Call(Actual, nameof(Cancel));
    internal object Collection(string name) => _runtime.Get(Actual, name)!;
    public void Dispose()
    {
        _runtime.Call(Actual, nameof(Dispose));
        Assert.Equal(0, Memory.Writes, "权限变化不得保存或清草稿");
    }
}

// Permission checks use no persistence. Expose only the integrated R010 contract; saving still fails closed.
internal sealed class PermissionMemorySettings : ISettingsService
{
    public AppSettings Settings { get; } = new();
    public int PageSize => 60;
    public bool HasUnsavedChanges => false;
    public event EventHandler? SaveStateChanged { add { } remove { } }
    internal int Writes { get; private set; }
    public ServerProfile ResolveServer(string url) => throw new AssertionException("禁止真实服务器配置");
    public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("禁止账号配置");
    public void Save() { Writes++; throw new AssertionException("禁止权限测试保存设置"); }
    public bool TrySave() { Writes++; throw new AssertionException("禁止权限测试保存设置"); }
    public void ExportPreferences(string path) => throw new AssertionException("禁止导出");
    public AppSettings ReadBackup(string path) => throw new AssertionException("禁止备份读取");
}
