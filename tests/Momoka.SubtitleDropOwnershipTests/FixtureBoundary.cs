using System.Reflection;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Mpv;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Momoka.Tests;

internal sealed class Fixture : IDisposable
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    internal TestUiQueue Ui { get; } = new();
    internal FakeSettings Settings { get; } = new();
    internal PlaybackService Service { get; }
    internal PlayerViewModel Vm { get; }
    internal PlayerPage Page { get; } = new();
    internal ShellPage Shell { get; } = new();
    internal HostWindow Window { get; } = new();
    internal FakeHandle A { get; } = new("A");
    internal List<(string Message, InfoBarSeverity Severity)> Notices { get; } = [];
    internal RingBufferLogSink LogSink { get; } = new();
    private readonly List<PlayerViewModel> _viewModels = [];
    private readonly List<FakeHandle> _handles = [];
    internal FakeHandle Current { get; private set; }

    internal Fixture(bool playing = true)
    {
        Settings.Settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        Settings.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        // No EmbySession, SettingsStore, vault, protector, identity restore or HTTP exists here.
        // The real service's command endpoint only needs _current. Any start is forbidden.
        Service = new PlaybackService(null!, Settings.Settings,
            () => throw new AssertionException("禁止启动真实后端"), new PlaybackPlanner(Settings.Settings, new ShaderGroupResolver(Settings.Settings.Shaders)), allowPlayback: false);
        Vm = NewVm();
        Current = A;
        _handles.Add(A);
        if (playing) Set(Service, "_current", A);
        Vm.SeedItem(new EmbyItem { Id = "A", Name = "匿名片A" });
        Page.Attach(Vm, Shell, Window);
        Log.UseSink(LogSink);
    }

    internal PlayerViewModel NewVm()
    {
        var vm = new PlayerViewModel(Service, Settings);
        vm.Noticed += (message, severity) => Notices.Add((message, severity));
        _viewModels.Add(vm);
        return vm;
    }

    internal FakeHandle SwitchFile(bool reuseHandle = false)
    {
        AdvancePlaybackGeneration();
        var replacement = reuseHandle ? Current : new FakeHandle("B");
        if (!reuseHandle) _handles.Add(replacement);
        replacement.Owner = "B";
        Current = replacement;
        Set(Service, "_current", replacement);
        Vm.SeedItem(new EmbyItem { Id = "B", Name = "匿名片B" });
        Vm.ChangeVmGeneration();
        return replacement;
    }

    internal void AdvancePlaybackGeneration() => Set(Service, "_playbackGeneration", Service.Generation + 1);
    internal void StopFile()
    {
        AdvancePlaybackGeneration();
        Set(Service, "_current", null);
    }
    internal void ReattachSameVm()
    {
        Page.Detach();
        Page.Attach(Vm, Shell, Window);
    }
    internal void ReplaceVm()
    {
        Page.Detach();
        var replacement = NewVm();
        replacement.SeedItem(new EmbyItem { Id = "B", Name = "匿名VM-B" });
        Page.Attach(replacement, Shell, Window);
    }

    internal DragEventArgs StartDrop(DataPackageView? data = null)
    {
        var args = new DragEventArgs(data ?? new DataPackageView());
        OnUi(() => Page.Drop(args));
        Ui.Drain();
        return args;
    }
    internal Task StartVmAdd(IReadOnlyList<string> paths, PlayerViewModel.InteractionContext context, Func<bool>? currentDrop = null)
    {
        Task? operation = null;
        OnUi(() => operation = Vm.AddCaptured(paths, context, currentDrop ?? (() => true)));
        return operation!;
    }
    internal void OnUi(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(Ui);
        try { action(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    internal void CompleteData(DragEventArgs args, params IStorageItem[] items)
    {
        args.DataView.Complete(items);
        Ui.Drain();
    }
    internal void Finish(CommandGate gate, bool accepted = true)
    {
        gate.Release(accepted);
        Ui.Drain();
    }
    internal void CheckEnded(DragEventArgs args)
    {
        Ui.Drain();
        Assert.Equal(1, args.Deferral.Completions, "拖拽deferral必须且只能在finally释放一次");
        Assert.Equal(0, Ui.Operations, "异步事件必须真正结束，不能遗留旧回调");
        Assert.Equal(0, Ui.Errors.Count, "无未处理async-void异常");
    }
    internal static void CheckTask(Task operation)
    {
        if (operation.IsFaulted) throw new AssertionException(operation.Exception!.ToString());
        Assert.True(operation.IsCompletedSuccessfully, "实际生产方法必须真正完成，不能用旧产物或等待中任务冒充");
    }
    private static void Set(object target, string field, object? value) =>
        (target.GetType().GetField(field, Fields) ?? throw new AssertionException("实际服务字段缺失：" + field)).SetValue(target, value);

    public void Dispose()
    {
        foreach (var handle in _handles) handle.ReleasePending();
        Ui.Drain();
        if (Page.Attached) Page.Detach();
        foreach (var vm in _viewModels) vm.CancelLifetime();
        Set(Service, "_current", null);
        Log.UseSink(NullLogSink.Instance);
        Assert.Equal(0, Settings.ForbiddenCalls, "本题不能进入配置／账号IO边界");
        Assert.Equal(0, _handles.Sum(handle => handle.ForbiddenCalls), "不允许被实际Core捕获后吞掉的旁支调用");
    }
}

internal sealed class FakeSettings : ISettingsService
{
    public AppSettings Settings { get; } = new();
    public int PageSize => 50;
    public bool HasUnsavedChanges => false;
    internal int ForbiddenCalls { get; private set; }
    private Exception Forbidden()
    {
        ForbiddenCalls++;
        return new AssertionException("禁止配置／账号／设置文件IO");
    }
    public ServerProfile ResolveServer(string url) => throw Forbidden();
    public AccountProfile ResolveAccount(ServerProfile server, string username) => throw Forbidden();
    public void Save() => throw Forbidden();
    public bool TrySave() => throw Forbidden();
    public void ExportPreferences(string path) => throw Forbidden();
    public AppSettings ReadBackup(string path) => throw Forbidden();
}

internal sealed class CommandGate
{
    private readonly TaskCompletionSource<bool> _completion = new();
    internal Task<bool> Task => _completion.Task;
    internal void Release(bool accepted) => _completion.TrySetResult(accepted);
}

internal sealed record RecordedCommand(string Owner, IReadOnlyList<string> Arguments);

internal sealed class FakeHandle(string owner) : IPlaybackHandle, IPlayerControl
{
    private readonly Queue<CommandGate> _waiting = new();
    private readonly List<CommandGate> _gates = [];
    internal string Owner { get; set; } = owner;
    internal List<RecordedCommand> Commands { get; } = [];
    internal Queue<bool> Answers { get; } = new();
    internal Action<int>? AtDispatch { get; set; }
    internal bool ThrowNext { get; set; }
    internal bool InHost { get; set; } = true;
    internal bool Channel { get; set; } = true;
    internal int ForbiddenCalls { get; private set; }
    internal CommandGate HoldNext()
    {
        var gate = new CommandGate();
        _waiting.Enqueue(gate);
        _gates.Add(gate);
        return gate;
    }
    internal int CommandsFor(string subject) => Commands.Count(command => command.Owner == subject);
    internal void ReleasePending()
    {
        foreach (var gate in _gates) gate.Release(false);
    }
    public bool HasControlChannel => Channel;
    public bool? PictureInHostWindow => InHost;
    public bool IsPaused => false;
    public PlayerStatus Status => new();
    public event Action<bool>? PauseChanged { add { } remove { } }
    public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
    public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
    public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!arguments.SequenceEqual(["sub-add", arguments.ElementAtOrDefault(1) ?? "", "select"]))
        {
            ForbiddenCalls++;
            throw new AssertionException("夹具只允许sub-add path select，不允许真实播放或其它命令");
        }
        Commands.Add(new RecordedCommand(Owner, [.. arguments]));
        AtDispatch?.Invoke(Commands.Count);
        if (ThrowNext)
        {
            ThrowNext = false;
            throw new InvalidOperationException("匿名命令失败");
        }
        if (_waiting.TryDequeue(out var gate)) return gate.Task;
        return Task.FromResult(Answers.TryDequeue(out var accepted) ? accepted : true);
    }
    private Exception Forbidden()
    {
        ForbiddenCalls++;
        return new AssertionException("禁止启动／停止／读媒体／native／字幕文件IO");
    }
    public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => throw Forbidden();
    public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken) => throw Forbidden();
    public Task StopAsync() => throw Forbidden();
    public Task ShowMessageAsync(string text) => throw Forbidden();
    public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken) => throw Forbidden();
    public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => throw Forbidden();
    public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => throw Forbidden();
    public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => throw Forbidden();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
