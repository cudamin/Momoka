using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Services;

namespace Momoka.Tests;

internal sealed class SaveFixture : IDisposable
{
    private readonly UiQueue _ui;
    private readonly EventHandler _changed;
    internal AppPaths Paths { get; }
    internal AppSettings Document { get; } = SettingsMigration.NewDefaults();
    internal SettingsStore Store { get; }
    internal SettingsService Service { get; }
    internal object ViewModel { get; }
    internal object Row { get; }
    internal SaveCounter Counter { get; } = new();
    internal int Captures { get; private set; }
    internal int AfterCalls { get; private set; }
    internal bool RejectPosts { get; set; }
    internal Action? After { get; set; }
    internal int UiThread { get; } = Environment.CurrentManagedThreadId;
    internal List<int> CaptureThreads { get; } = [];
    internal List<double> CaptureMilliseconds { get; } = [];
    internal ConcurrentQueue<bool> Notifications { get; } = new();
    internal object DiskGate => typeof(SettingsStore).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Store)!;

    internal SaveFixture(UiQueue ui)
    {
        _ui = ui;
        var root = Path.Combine(ShellRuntime.Current.SourceRoot, "artifacts", "perf-settings", "data", Guid.NewGuid().ToString("N"));
        Paths = new(root);
        Directory.CreateDirectory(root);
        Document.Playback.SubtitleScalePercent = 50;
        Store = new(Paths, new DenySecrets());
        Service = new(Store, Document);
        Service.ConfigureDeferredSave(work =>
        {
            if (RejectPosts) return false;
            _ui.Post(_ =>
            {
                CaptureThreads.Add(Environment.CurrentManagedThreadId);
                var started = Stopwatch.GetTimestamp();
                Captures++;
                work();
                CaptureMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }, null);
            return true;
        });
        Log.UseSink(Counter);
        ViewModel = ShellRuntime.Current.New("SettingsViewModel");
        ShellRuntime.Current.Attach(ViewModel, Service, Paths, null!);
        Row = ShellRuntime.Current.Invoke(ViewModel, "Slider", "连续输入", 50d, 300d, 1d,
            (Func<double>)(() => Document.Playback.SubtitleScalePercent),
            (Action<double>)(value => Document.Playback.SubtitleScalePercent = (int)value),
            null, "", (Action)(() => { AfterCalls++; After?.Invoke(); }))!;
        _changed = (_, _) =>
        {
            Notifications.Enqueue(Service.HasUnsavedChanges);
            _ui.Post(_ => ShellRuntime.Current.Invoke(ViewModel, "SyncSaveState"), null);
        };
        Service.SaveStateChanged += _changed;
    }

    internal double Value
    {
        get => ShellRuntime.Current.Get<double>(Row, "Value");
        set => ShellRuntime.Current.Set(Row, "Value", value);
    }
    internal bool VmUnsaved => ShellRuntime.Current.Get<bool>(ViewModel, "HasUnsavedChanges");
    internal AppSettings Read() => Store.LoadStrict();
    internal async Task<bool> Flush() => await Service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
    internal async Task WaitFor(Func<bool> predicate)
    {
        var deadline = Stopwatch.StartNew();
        while (!predicate())
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(8), "定向保存夹具没有收敛");
            await Task.Delay(5);
        }
    }
    public void Dispose()
    {
        Service.SaveStateChanged -= _changed;
        Service.Dispose();
        Log.UseSink(NullLogSink.Instance);
        // 留在本树 artifacts 的匿名文件是证据，不递归清理或碰真实数据。
    }
}

internal sealed class SaveCounter : ILogSink
{
    private int _writes;
    internal int Writes => Volatile.Read(ref _writes);
    internal Action? OnWrite { get; set; }
    internal Action? OnFailure { get; set; }
    internal ConcurrentQueue<int> WriteThreads { get; } = new();
    internal ConcurrentQueue<LogEntry> Entries { get; } = new();
    public void Write(in LogEntry entry)
    {
        Entries.Enqueue(entry);
        if (entry.Category == "settings" && entry.Level == LogLevel.Error && entry.Message == "保存设置失败")
            OnFailure?.Invoke();
        if (entry.Category != "settings" || entry.Level != LogLevel.Debug || entry.Message != "设置已保存") return;
        Interlocked.Increment(ref _writes);
        WriteThreads.Enqueue(Environment.CurrentManagedThreadId);
        OnWrite?.Invoke();
    }
}
