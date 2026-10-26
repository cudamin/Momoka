using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>Only load the explicitly supplied DLL underneath the current tree; never run its entry point.</summary>
internal sealed class ShellRuntime
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Assembly _shell;
    internal static ShellRuntime Current { get; private set; } = null!;
    internal object? NextPageViewModel { get; set; }
    internal object TakePageViewModel()
    {
        var vm = NextPageViewModel ?? New("SettingsViewModel");
        NextPageViewModel = null;
        return vm;
    }
    public string SourceRoot { get; }

    public ShellRuntime(string assemblyPath, string sourceRoot)
    {
        SourceRoot = Path.GetFullPath(sourceRoot);
        var shellPath = Path.GetFullPath(assemblyPath);
        var expected = Path.Combine(SourceRoot, "src", "Momoka.Shell", "bin", "x64", "Release",
            "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        Assert.Equal(expected, shellPath, "只允许本树标准 Release Shell 输出");
        Assert.True(File.Exists(shellPath), "必须先成功编译当前 Shell");
        var directory = Path.GetDirectoryName(shellPath)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        _shell = AssemblyLoadContext.Default.LoadFromAssemblyPath(shellPath);
        Current = this;
    }

    public Type Type(string name) => _shell.GetType("Momoka.Shell.ViewModels." + name, throwOnError: true)!;
    public object New(string name, params object?[] args) => Activator.CreateInstance(Type(name), Members, null, args, null)!;
    public object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, Members)
            ?? throw new AssertionException("真实 Shell 缺少入口 " + name);
        try { return method.Invoke(target, CompleteOptionalArguments(method, args)); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    internal static object?[] CompleteOptionalArguments(MethodInfo method, IReadOnlyList<object?> supplied)
    {
        var parameters = method.GetParameters();
        if (supplied.Count > parameters.Length)
            throw new AssertionException("真实 Shell 入口参数过多：" + method.Name);
        var completed = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index < supplied.Count) completed[index] = supplied[index];
            else if (parameters[index].HasDefaultValue) completed[index] = parameters[index].DefaultValue;
            else throw new AssertionException("真实 Shell 缺少必需参数：" + parameters[index].Name);
        }
        return completed;
    }

    public T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    public void Set(object target, string name, object? value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
    public IList List(object target, string name) => Get<IList>(target, name);

    public void Attach(object vm, ISettingsService settings, AppPaths paths, IServerCapabilities capabilities)
    {
        var args = Type("SettingsViewModel").GetMethod("Attach", Members)!.GetParameters().Select(parameter => parameter.Name switch
        {
            "settings" => (object)settings,
            "paths" => paths,
            "capabilities" => capabilities,
            _ => parameter.HasDefaultValue ? parameter.DefaultValue : null
        }).ToArray();
        Invoke(vm, "Attach", args);
    }

    public object Draft(object vm)
    {
        var row = New("SettingTextRow", "草稿", "不执行保存", "", "尚未输完",
            (Func<string, string>)(value => value), (Action)(() => { }));
        var rows = Array.CreateInstance(Type("SettingRow"), 1);
        rows.SetValue(row, 0);
        var section = New("SettingSection", "主页", "离线草稿卡", "只验证缓存身份", rows);
        List(vm, "Sections").Add(section);
        Set(vm, "IsReady", true);
        return row;
    }
}

internal sealed class MemorySettings : ISettingsService
{
    public AppSettings Settings { get; } = new();
    public int PageSize => 60;
    public event EventHandler? SaveStateChanged;
    public int SaveSubscribers => SaveStateChanged?.GetInvocationList().Length ?? 0;
    public void CompleteSave(bool unsaved)
    {
        HasUnsavedChanges = unsaved;
        SaveStateChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool HasUnsavedChanges { get; set; }
    public int Writes { get; private set; }
    public ServerProfile ResolveServer(string url) => throw new InvalidOperationException("禁止服务器配置操作");
    public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException("禁止账号配置操作");
    public void Save() => Writes++;
    public bool TrySave() { Writes++; return true; }
    public void ExportPreferences(string path) => throw new InvalidOperationException("禁止导出");
    public AppSettings ReadBackup(string path) => throw new InvalidOperationException("禁止导入");
}

internal sealed class DenySecrets : ISecretProtector
{
    public string Protect(string plainText) => throw new InvalidOperationException("夹具禁止任何凭据写入");
    public string Unprotect(string cipherText) => throw new InvalidOperationException("夹具禁止任何凭据读取");
}
