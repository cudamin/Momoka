using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Shell;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static object? _burst;
    private static object? _warmBurst;
    private static object? _synchronousControl;
    private static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("必须显式传本树当前 Release Shell DLL 与根目录");
        _ = new ShellRuntime(args[0], args[1]);
        Case("正式VM/生成Slider连续150值：一捕获一落盘，最后值与after实时", async fixture =>
        {
            Assert.Equal(0, fixture.Counter.Writes);
            Assert.Equal(0, fixture.Store.PendingVersion);
            var times = new List<double>();
            for (var value = 51; value <= 200; value++)
            {
                var started = Stopwatch.GetTimestamp();
                fixture.Value = value;
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                Assert.Equal(value, fixture.Document.Playback.SubtitleScalePercent);
                Assert.Equal(value - 50, fixture.AfterCalls);
            }
            Assert.Equal(0, fixture.Counter.Writes, "热路径无逐值 IO");
            Assert.Equal(0, fixture.Captures, "热路径无逐值 clone/serialize");
            Assert.Equal(150L, fixture.Store.PendingVersion);
            Assert.Equal("200", ShellRuntime.Current.Get<string>(fixture.Row, "ValueLabel"));
            Assert.True(await fixture.Flush());
            Assert.Equal(1, fixture.Counter.Writes);
            Assert.Equal(1, fixture.Captures);
            Assert.Equal(200, fixture.Read().Playback.SubtitleScalePercent);
            Assert.Equal(0L, fixture.Store.PendingVersion);
            Assert.False(fixture.Service.HasUnsavedChanges);
            Assert.True(fixture.CaptureThreads.All(thread => thread == fixture.UiThread));
            Assert.True(fixture.Counter.WriteThreads.All(thread => thread != fixture.UiThread));
            _burst = new
            {
                values = 150,
                saves = fixture.Counter.Writes,
                snapshotCaptures = fixture.Captures,
                afterCalls = fixture.AfterCalls,
                totalPropertySetMs = times.Sum(),
                medianPropertySetMs = times.Order().ElementAt(75),
                maxPropertySetMs = times.Max(),
                captureMs = fixture.CaptureMilliseconds.Sum(),
                finalValue = fixture.Read().Playback.SubtitleScalePercent,
                uiThread = fixture.UiThread,
                writeThreads = fixture.Counter.WriteThreads.ToArray()
            };
            Console.WriteLine("PERF-03 " + JsonSerializer.Serialize(_burst));
        });
        Case("同进程热路径150值仍一捕获一落盘，记录一次捕获UI成本", async fixture =>
        {
            var times = new List<double>();
            for (var value = 51; value <= 200; value++)
            {
                var started = Stopwatch.GetTimestamp();
                fixture.Value = value;
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            Assert.Equal(0, fixture.Captures);
            Assert.True(await fixture.Flush());
            Assert.Equal(1, fixture.Captures);
            Assert.Equal(1, fixture.Counter.Writes);
            Assert.Equal(200, fixture.Read().Playback.SubtitleScalePercent);
            _warmBurst = new
            {
                values = 150,
                saves = fixture.Counter.Writes,
                snapshotCaptures = fixture.Captures,
                totalPropertySetMs = times.Sum(),
                medianPropertySetMs = times.Order().ElementAt(75),
                maxPropertySetMs = times.Max(),
                captureMs = fixture.CaptureMilliseconds.Sum(),
                finalValue = fixture.Read().Playback.SubtitleScalePercent
            };
            Console.WriteLine("PERF-03-WARM " + JsonSerializer.Serialize(_warmBurst));
        });
        Case("当前生产同步Save对照150值确实150落盘，计时不冒称旧版本重跑", fixture =>
        {
            var after = 0;
            var row = ShellRuntime.Current.New("SettingSliderRow", "同步对照", null, 50d, 300d, 1d, 50d,
                (Action<double>)(value => fixture.Document.Playback.SubtitleScalePercent = (int)value),
                (Action)fixture.Service.Save, (Action)(() => after++));
            var times = new List<double>();
            for (var value = 51; value <= 200; value++)
            {
                var started = Stopwatch.GetTimestamp();
                ShellRuntime.Current.Set(row, "Value", (double)value);
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            Assert.Equal(150, fixture.Counter.Writes);
            Assert.Equal(150, after);
            Assert.Equal(0, fixture.Captures);
            Assert.Equal(200, fixture.Read().Playback.SubtitleScalePercent);
            _synchronousControl = new
            {
                values = 150,
                saves = fixture.Counter.Writes,
                afterCalls = after,
                totalPropertySetMs = times.Sum(),
                medianPropertySetMs = times.Order().ElementAt(75),
                maxPropertySetMs = times.Max(),
                kind = "current_production_row_with_explicit_synchronous_save_delegate_not_historical_binary"
            };
            Console.WriteLine("PERF-03-SYNC-CONTROL " + JsonSerializer.Serialize(_synchronousControl));
            return Task.CompletedTask;
        });
        Case("构造/同值/Reseed只更新UI，不登记、不保存", async fixture =>
        {
            fixture.Value = 50;
            ShellRuntime.Current.Invoke(fixture.Row, "Reseed", 90d);
            fixture.Value = 90;
            Assert.Equal(50, fixture.Document.Playback.SubtitleScalePercent);
            Assert.Equal(0, fixture.AfterCalls);
            Assert.Equal(0L, fixture.Store.PendingVersion);
            Assert.True(await fixture.Flush());
            Assert.Equal(0, fixture.Captures);
            Assert.Equal(0, fixture.Counter.Writes);
            Assert.False(File.Exists(fixture.Paths.SettingsFile));
        });
        Case("实际UnitSlider同样合并且字符串最后值可恢复", async fixture =>
        {
            var unit = "";
            var row = ShellRuntime.Current.Invoke(fixture.ViewModel, "UnitSlider", "描边大小", 1.65d,
                (Func<string>)(() => unit), (Action<string>)(value => { unit = value; fixture.Document.Playback.SubtitleBorderSize = value; }),
                null, "", (Action)(() => { }))!;
            for (var index = 1; index <= 150; index++) ShellRuntime.Current.Set(row, "Value", index / 20d);
            Assert.Equal(0, fixture.Counter.Writes);
            Assert.True(await fixture.Flush());
            Assert.Equal(1, fixture.Counter.Writes);
            Assert.Equal("7.5", fixture.Read().Playback.SubtitleBorderSize);
        });
        Case("after重入另一个Value仍保留最终代次，不追加旧值", async fixture =>
        {
            fixture.After = () => { if (fixture.Value == 60) fixture.Value = 80; };
            fixture.Value = 60;
            Assert.Equal(80, fixture.Document.Playback.SubtitleScalePercent);
            Assert.Equal(2, fixture.AfterCalls);
            Assert.True(await fixture.Flush());
            Assert.Equal(80, fixture.Read().Playback.SubtitleScalePercent);
            Assert.Equal(1, fixture.Counter.Writes);
        });
        Case("无显式flush也会在静默后自动保存", async fixture =>
        {
            fixture.Value = 170;
            await fixture.WaitFor(() => fixture.Counter.Writes == 1 && fixture.Store.PendingVersion == 0);
            Assert.Equal(170, fixture.Read().Playback.SubtitleScalePercent);
            Assert.Equal(1, fixture.Captures);
        });
        Case("连续输入超过静默期仍有最长1s合并边界，无每值捕获", async fixture =>
        {
            for (var index = 1; index <= 100; index++)
            {
                fixture.Value = 50 + index;
                await Task.Delay(25);
                if (index == 80) Assert.True(fixture.Counter.Writes >= 1, "不停输入不能无限延期");
            }
            Assert.True(await fixture.Flush());
            Assert.Equal(150, fixture.Read().Playback.SubtitleScalePercent);
            Assert.True(fixture.Captures < 10, "100次编辑最多有限合并，不应每值克隆");
            Assert.True(fixture.Counter.Writes < 10);
        });
        Case("并发Flush共用排空，最后值完成前不提前返回", async fixture =>
        {
            fixture.Value = 130;
            var a = fixture.Service.FlushAsync();
            var b = fixture.Service.FlushAsync();
            Assert.True(ReferenceEquals(a, b));
            Assert.False(a.IsCompleted);
            Assert.True(await a.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(130, fixture.Read().Playback.SubtitleScalePercent);
            Assert.Equal(1, fixture.Counter.Writes);
        });
        Case("真正文件锁失败保留欠项/通知，重试实际当前文档恢复", async fixture =>
        {
            Assert.True(fixture.Service.TrySave());
            using (var locked = new FileStream(fixture.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fixture.Value = 190;
                Assert.False(await fixture.Flush());
                Assert.True(fixture.Service.HasUnsavedChanges);
                Assert.True(fixture.Store.PendingVersion > 0);
                await fixture.WaitFor(() => fixture.VmUnsaved);
                Assert.True(fixture.Notifications.Contains(true));
                Assert.Equal(190, fixture.Document.Playback.SubtitleScalePercent);
            }
            var retry = ShellRuntime.Current.Get<ICommand>(fixture.ViewModel, "RetrySaveCommand");
            Assert.True(retry.CanExecute(null));
            retry.Execute(null);
            Assert.False(fixture.Service.HasUnsavedChanges);
            Assert.False(fixture.VmUnsaved);
            Assert.True(await fixture.Flush());
            Assert.Equal(190, fixture.Read().Playback.SubtitleScalePercent);
            Assert.Equal(0L, fixture.Store.PendingVersion);
        });
        Case("快照序列化失败保留欠项，修正后flush恢复而非取消debounce", async fixture =>
        {
            fixture.Document.Danmaku.Opacity = double.NaN;
            fixture.Service.SaveDeferred();
            Assert.False(await fixture.Flush());
            Assert.True(fixture.Service.HasUnsavedChanges);
            Assert.True(fixture.Store.PendingVersion > 0);
            Assert.Equal(0, fixture.Counter.Writes);
            fixture.Document.Danmaku.Opacity = 70;
            Assert.True(await fixture.Flush());
            Assert.False(fixture.Service.HasUnsavedChanges);
            Assert.Equal(70d, fixture.Read().Danmaku.Opacity);
        });
        Case("捕获失败通知重入新滑块编辑继续保存，旧失败不清新代次", async fixture =>
        {
            fixture.Document.Danmaku.Opacity = double.NaN;
            var recovered = false;
            EventHandler handler = (_, _) =>
            {
                if (recovered || !fixture.Service.HasUnsavedChanges) return;
                Assert.Equal(fixture.UiThread, Environment.CurrentManagedThreadId);
                recovered = true;
                fixture.Document.Danmaku.Opacity = 70;
                fixture.Value = 195;
            };
            fixture.Service.SaveStateChanged += handler;
            fixture.Value = 100;
            try
            {
                Assert.True(await fixture.Flush());
                Assert.True(recovered);
                Assert.Equal(195, fixture.Read().Playback.SubtitleScalePercent);
                Assert.Equal(2, fixture.Captures);
                Assert.Equal(1, fixture.Counter.Writes);
                Assert.Equal(1, fixture.Counter.Entries.Count(entry => entry.Category == "settings" && entry.Level == LogLevel.Error));
                Assert.Equal(0L, fixture.Store.PendingVersion);
                Assert.False(fixture.Service.HasUnsavedChanges);
            }
            finally { fixture.Service.SaveStateChanged -= handler; }
        });
        Case("UI捕获队列拒绝保留欠项；队列恢复可重试最后值", async fixture =>
        {
            fixture.RejectPosts = true;
            fixture.Value = 155;
            Assert.False(await fixture.Flush());
            Assert.True(fixture.Service.HasUnsavedChanges);
            Assert.Equal(0, fixture.Captures);
            fixture.RejectPosts = false;
            Assert.True(await fixture.Flush());
            Assert.Equal(155, fixture.Read().Playback.SubtitleScalePercent);
        });
        Case("等待磁盘锁时UI编辑不等IO，过期代次跳过再排空最新值", async fixture =>
        {
            using var release = new ManualResetEventSlim();
            var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var holder = Task.Run(() => { lock (fixture.DiskGate) { acquired.SetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); } });
            await acquired.Task;
            try
            {
                fixture.Value = 60;
                var flush = fixture.Service.FlushAsync();
                await fixture.WaitFor(() => fixture.Captures == 1);
                var started = Stopwatch.GetTimestamp();
                fixture.Value = 80;
                Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(250), "编辑热路径不拿磁盘锁");
                Assert.False(flush.IsCompleted);
                release.Set();
                Assert.True(await flush.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(80, fixture.Read().Playback.SubtitleScalePercent);
                Assert.Equal(1, fixture.Counter.Writes, "旧快照跳过，只有最后代次写盘");
                Assert.Equal(2, fixture.Captures);
            }
            finally { release.Set(); await holder; }
        });
        Case("旧写已提交但完成回调延迟时的新编辑不会丢失或被提前flush", async fixture =>
        {
            using var release = new ManualResetEventSlim();
            var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var intercepted = 0;
            fixture.Counter.OnWrite = () =>
            {
                if (Interlocked.Exchange(ref intercepted, 1) != 0) return;
                committed.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            };
            fixture.Value = 100;
            var flush = fixture.Service.FlushAsync();
            try
            {
                await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                fixture.Value = 160;
                fixture.Document.Ui.SidebarMode = true;
                Assert.False(flush.IsCompleted);
                Assert.True(fixture.Store.PendingVersion > 0);
                release.Set();
                Assert.True(await flush.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(160, fixture.Read().Playback.SubtitleScalePercent);
                Assert.True(fixture.Read().Ui.SidebarMode);
                Assert.Equal(2, fixture.Counter.Writes);
            }
            finally { release.Set(); }
        });
        Case("旧写失败收尾前的新编辑继续排空，不取消新的保存请求", async fixture =>
        {
            Assert.True(fixture.Service.TrySave());
            using var locked = new FileStream(fixture.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var release = new ManualResetEventSlim();
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var intercepted = 0;
            fixture.Counter.OnFailure = () =>
            {
                if (Interlocked.Exchange(ref intercepted, 1) != 0) return;
                failed.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            };
            fixture.Value = 100;
            var flush = fixture.Service.FlushAsync();
            try
            {
                await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(fixture.Service.HasUnsavedChanges);
                fixture.Value = 190;
                fixture.Document.Ui.SidebarMode = true;
                Assert.False(flush.IsCompleted);
                locked.Dispose();
                release.Set();
                Assert.True(await flush.WaitAsync(TimeSpan.FromSeconds(10)), "旧失败不得取消已登记的新编辑");
                Assert.Equal(190, fixture.Read().Playback.SubtitleScalePercent);
                Assert.True(fixture.Read().Ui.SidebarMode);
                Assert.Equal(2, fixture.Captures);
                Assert.Equal(2, fixture.Counter.Writes, "初始文档加最终新文档，失败尝试不是成功落盘");
                Assert.Equal(1, fixture.Counter.Entries.Count(entry => entry.Category == "settings" && entry.Level == LogLevel.Error));
                Assert.Equal(0L, fixture.Store.PendingVersion);
                Assert.False(fixture.Service.HasUnsavedChanges);
            }
            finally { release.Set(); fixture.Counter.OnFailure = null; }
        });
        Case("失败后静止不循环重试；新编辑触发恢复且保存最终值", async fixture =>
        {
            fixture.Document.Danmaku.Opacity = double.NaN;
            fixture.Value = 100;
            Assert.False(await fixture.Flush());
            var captures = fixture.Captures;
            await Task.Delay(450);
            Assert.Equal(captures, fixture.Captures);
            Assert.True(fixture.Service.HasUnsavedChanges);
            fixture.Document.Danmaku.Opacity = 70;
            fixture.Value = 190;
            Assert.True(await fixture.Flush());
            Assert.Equal(190, fixture.Read().Playback.SubtitleScalePercent);
            Assert.False(fixture.Service.HasUnsavedChanges);
        });
        Case("稳定快照后修改活对象/列表不被Normalize写回，后台不读新列表", async fixture =>
        {
            fixture.Value = 75;
            var originalServers = fixture.Document.Servers.Count;
            var snapshot = fixture.Store.CapturePendingSave(fixture.Document)!;
            fixture.Document.Ui.PageSize = 280;
            fixture.Document.Servers.Add(new() { Id = "anonymous", Name = "仅内存服务器", Url = "http://offline.invalid" });
            Assert.True(await Task.Run(() => fixture.Store.TrySaveSnapshot(snapshot)));
            Assert.Equal(280, fixture.Document.Ui.PageSize);
            Assert.Equal(originalServers + 1, fixture.Document.Servers.Count);
            Assert.Equal(originalServers, fixture.Read().Servers.Count, "已捕获文档不再枚举活列表");
            Assert.True(fixture.Service.TrySave());
            Assert.Equal(280, fixture.Read().Ui.PageSize);
            Assert.Equal(originalServers + 1, fixture.Read().Servers.Count);
        });
        Case("非滑块同步编辑胜过旧延迟快照，不丢侧栏与滑块最终值", async fixture =>
        {
            fixture.Value = 60;
            var old = fixture.Store.CapturePendingSave(fixture.Document)!;
            fixture.Value = 90;
            fixture.Document.Ui.SidebarMode = true;
            Assert.True(fixture.Service.TrySave());
            var writes = fixture.Counter.Writes;
            Assert.True(await Task.Run(() => fixture.Store.TrySaveSnapshot(old)));
            Assert.Equal(writes, fixture.Counter.Writes);
            Assert.True(await fixture.Flush());
            Assert.True(fixture.Read().Ui.SidebarMode);
            Assert.Equal(90, fixture.Read().Playback.SubtitleScalePercent);
        });
        Case("旧保存失败通知重入较新同步成功，欠项/flush不能被旧失败覆盖", async fixture =>
        {
            fixture.Document.Danmaku.Opacity = double.NaN;
            var recovered = false;
            EventHandler handler = (_, _) =>
            {
                if (recovered || !fixture.Service.HasUnsavedChanges) return;
                recovered = true;
                fixture.Document.Danmaku.Opacity = 70;
                Assert.True(fixture.Service.TrySave());
            };
            fixture.Service.SaveStateChanged += handler;
            fixture.Service.SaveDeferred();
            try
            {
                Assert.True(await fixture.Flush());
                Assert.True(recovered);
                Assert.False(fixture.Service.HasUnsavedChanges);
                Assert.Equal(0L, fixture.Store.PendingVersion);
            }
            finally { fixture.Service.SaveStateChanged -= handler; }
        });
        Case("SettingsWindow实际Hide→正式VM.Cancel触发排空，缓存重入无补写", async fixture =>
        {
            fixture.Value = 165;
            var window = new FlushWindowFixture(() => ShellRuntime.Current.Invoke(fixture.ViewModel, "Cancel"));
            window.Hide();
            Assert.True(window.IsHidden);
            Assert.True(await fixture.Flush());
            Assert.Equal(165, fixture.Read().Playback.SubtitleScalePercent);
            var writes = fixture.Counter.Writes;
            ShellRuntime.Current.Invoke(fixture.ViewModel, "Activate");
            ShellRuntime.Current.Invoke(fixture.ViewModel, "SyncSaveState");
            await Task.Delay(230);
            Assert.Equal(writes, fixture.Counter.Writes);
            Assert.False(fixture.VmUnsaved);
        });
        Case("实际App关窗前置等待flush，最终同步保存仍覆盖窗口/非滑块尾项", async fixture =>
        {
            fixture.Value = 175;
            var app = new FlushAppFixture(fixture.Service);
            var prepare = app.Prepare();
            Assert.False(prepare.IsCompleted);
            app.ShutdownBoundary.Completion.SetResult();
            await prepare.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(175, fixture.Read().Playback.SubtitleScalePercent);
            fixture.Document.Ui.SidebarMode = true;
            app.Close();
            Assert.True(app.Remembered);
            Assert.True(app.Exited);
            Assert.True(app.ContainerBoundary.Disposed);
            Assert.True(fixture.Read().Ui.SidebarMode);
        });
        Case("播放收尾异常仍执行真实App finally的设置flush", async fixture =>
        {
            fixture.Value = 180;
            var app = new FlushAppFixture(fixture.Service);
            var prepare = app.Prepare();
            app.ShutdownBoundary.Completion.SetException(new IOException("假播放收尾失败"));
            try { await prepare.WaitAsync(TimeSpan.FromSeconds(10)); throw new AssertionException("应保留播放异常"); }
            catch (IOException) { }
            Assert.Equal(180, fixture.Read().Playback.SubtitleScalePercent);
            Assert.Equal(0L, fixture.Store.PendingVersion);
        });
        Case("退出flush失败留真实欠项和错误日志，不报告成功", async fixture =>
        {
            fixture.RejectPosts = true;
            fixture.Value = 185;
            var app = new FlushAppFixture(fixture.Service);
            app.ShutdownBoundary.Completion.SetResult();
            await app.Prepare().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(fixture.Service.HasUnsavedChanges);
            Assert.True(fixture.Counter.Entries.Any(entry => entry.Message.Contains("退出前设置排空失败", StringComparison.Ordinal)));
            fixture.RejectPosts = false;
            Assert.True(await fixture.Flush());
        });
        var result = Run();
        var evidence = Path.Combine(ShellRuntime.Current.SourceRoot, "artifacts", "perf-settings");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "slider-results.json"), JsonSerializer.Serialize(new
        {
            sourceRoot = args[1],
            shell = args[0],
            shellSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),
            kind = "actual_release_shell_vm_generated_slider_and_production_store_queue",
            result,
            coldBurst = _burst,
            warmBurst = _warmBurst,
            synchronousControl = _synchronousControl
        }, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }

    private static void Case(string name, Func<SaveFixture, Task> test) => Test(name, () =>
    {
        using var ui = new UiQueue();
        ui.Run(async () => { using var fixture = new SaveFixture(ui); await test(fixture); });
    });
}
