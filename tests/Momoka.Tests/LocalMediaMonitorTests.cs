using static Momoka.Tests.TestHarness;
using Momoka.LocalMedia;
using System.Diagnostics;

namespace Momoka.Tests;

#pragma warning disable RS0030 // The console harness joins the real asynchronous monitor tests.
public static class LocalMediaMonitorTests
{
    public static void Register()
    {
        Test("本地媒体库：后台监控和持久索引能力可用", () =>
        {
            var assembly = typeof(Momoka.Configuration.AppSettings).Assembly;
            Assert.NotNull(assembly.GetType("Momoka.LocalMedia.LocalMediaMonitor"), "自动整理必须由后台监控拥有生命周期");
            Assert.NotNull(assembly.GetType("Momoka.LocalMedia.LocalMediaLibrary"), "成功整理应进入持久本地媒体库");
        });

        Test("本地媒体库：实际成功执行才入库并持久化且各库独立", () => Run(async () =>
        {
            using var files = new Files();
            var source = files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            var plan = LocalMediaOrganizer.Prepare(source, files.Target, Movie(), LocalMediaLinkMode.HardLink);
            Assert.True((await library.ExecuteAsync("one", plan, Image, CancellationToken.None)).Success);
            var entry = library.Entries.Single();
            Assert.Equal("one", entry.LibraryId);
            Assert.True(entry.IsInLibrary);
            Assert.True(entry.Complete);
            Assert.True(File.Exists(entry.TargetPath));
            var loaded = new LocalMediaLibrary(files.Index);
            Assert.Equal(entry.Id, loaded.Entries.Single().Id);
            var second = LocalMediaOrganizer.Prepare(source, Path.Combine(files.Target, "second"), Movie(), LocalMediaLinkMode.HardLink);
            Assert.True((await loaded.ExecuteAsync("two", second, Image, CancellationToken.None)).Success);
            Assert.Equal(2, loaded.Entries.Count);
        }));

        Test("本地媒体库：图片失败仍入库但显示待补且重启不重放", () => Run(async () =>
        {
            using var files = new Files();
            var source = files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            var plan = LocalMediaOrganizer.Prepare(source, files.Target, Movie() with { PosterUrl = "https://example.invalid/image.jpg?secret=private" }, LocalMediaLinkMode.HardLink);
            var result = await library.ExecuteAsync("one", plan, (_, _) => Task.FromException<byte[]>(new IOException("图片失败")), CancellationToken.None);
            Assert.False(result.Success);
            Assert.True(library.Entries.Single().IsInLibrary);
            Assert.False(library.Entries.Single().Complete);
            Assert.DoesNotContain("private", File.ReadAllText(files.Index));
            var calls = 0;
            await using var monitor = new LocalMediaMonitor(new(files.Index), (_, _) => { calls++; return Task.FromResult(Movie()); }, Image);
            await monitor.StartAsync(files.Options());
            await Task.Delay(500);
            Assert.Equal(0, calls);
        }));

        Test("本地监控：真实创建事件等待分段写稳定后建立链接及NFO", () => Run(async () =>
        {
            using var files = new Files();
            var library = new LocalMediaLibrary(files.Index);
            var calls = 0;
            await using var monitor = new LocalMediaMonitor(library, (_, _) => { calls++; return Task.FromResult(Movie()); }, Image);
            await monitor.StartAsync(files.Options() with { IncludeExisting = false });
            var video = Path.Combine(files.Source, "incoming.mkv");
            using (var stream = new FileStream(video, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            {
                stream.Write([1, 2]); stream.Flush();
                await Task.Delay(350);
                Assert.Equal(0, calls);
                stream.Write([3, 4]); stream.Flush();
                await Task.Delay(350);
                Assert.Equal(0, calls, "仍有写入句柄时不能识别或整理");
            }
            await Until(() => library.Entries.Count == 1);
            Assert.Equal(1, calls);
            Assert.True(library.Entries[0].IsInLibrary);
            Assert.True(File.Exists(Path.ChangeExtension(library.Entries[0].TargetPath, ".nfo")));
            File.SetLastWriteTimeUtc(video, DateTime.UtcNow);
            await Task.Delay(500);
            Assert.Equal(1, calls, "重复事件不重复处理已经尝试的文件");
        }));

        Test("本地监控：已有文件默认处理并且改名自事件不重复", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("before.mkv");
            var library = new LocalMediaLibrary(files.Index);
            var calls = 0;
            await using var monitor = new LocalMediaMonitor(library, (_, _) => { calls++; return Task.FromResult(Movie()); }, Image);
            await monitor.StartAsync(files.Options() with { Mode = LocalMediaLinkMode.Rename });
            await Until(() => library.Entries.Count == 1);
            await Task.Delay(450);
            Assert.Equal(1, calls);
            Assert.Equal("Film (2026).mkv", Path.GetFileName(library.Entries[0].TargetPath));
        }));

        Test("本地监控：从临时后缀改名为视频触发真实事件", () => Run(async () =>
        {
            using var files = new Files();
            var partial = Path.Combine(files.Source, "download.part");
            File.WriteAllText(partial, "video");
            var library = new LocalMediaLibrary(files.Index);
            await using var monitor = new LocalMediaMonitor(library, (_, _) => Task.FromResult(Movie()), Image);
            await monitor.StartAsync(files.Options() with { IncludeExisting = false });
            File.Move(partial, Path.Combine(files.Source, "download.mkv"));
            await Until(() => library.Entries.Count == 1);
            Assert.True(library.Entries.Single().IsInLibrary);
        }));

        Test("本地监控：识别失败不写不定时重试刷新仅重新读", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            var calls = 0;
            await using var monitor = new LocalMediaMonitor(library, (_, _) => { calls++; return Task.FromException<LocalMediaMetadata>(new InvalidOperationException("身份不唯一")); }, Image);
            await monitor.StartAsync(files.Options());
            await Until(() => calls == 1);
            await Task.Delay(500);
            Assert.Equal(1, calls);
            Assert.False(Directory.Exists(files.Target));
            Assert.Equal(0, library.Entries.Count);
            monitor.Refresh();
            await Until(() => calls == 2);
        }));

        Test("本地监控：停止取消迟到识别并保证零写", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("film.mkv");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<LocalMediaMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
            var library = new LocalMediaLibrary(files.Index);
            await using var monitor = new LocalMediaMonitor(library, (_, _) => { started.TrySetResult(); return release.Task; }, Image);
            await monitor.StartAsync(files.Options());
            await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var stop = monitor.StopAsync();
            release.SetResult(Movie());
            await stop;
            Assert.False(Directory.Exists(files.Target));
            Assert.Equal(0, library.Entries.Count);
            files.Video("later.mkv");
            await Task.Delay(350);
            Assert.False(Directory.Exists(files.Target));
        }));

        Test("本地监控：网络目录硬链接在启动前拒绝", () => Run(async () =>
        {
            using var files = new Files();
            await using var monitor = new LocalMediaMonitor(new(files.Index), (_, _) => Task.FromResult(Movie()), Image);
            var error = Assert.Catch<IOException>(() => monitor.StartAsync(
                new("one", @"\\server\share\source", @"\\server\share\target", LocalMediaLinkMode.HardLink)).GetAwaiter().GetResult());
            Assert.Contains("网络目录不支持硬链接", error.Message);
            Assert.False(Directory.Exists(files.Target));
        }));

        Test("本地监控：回环网络共享上的新建文件自动改名", () => Run(async () =>
        {
            var share = LoopbackShare(Path.GetTempPath());
            if (share is null)
            {
                Skip("本地监控：回环网络共享上的新建文件自动改名", "本机没有可访问的回环 SMB 管理共享");
                return;
            }
            var root = Path.Combine(share, "momoka-unc-monitor-" + Guid.NewGuid().ToString("N"));
            var index = Path.Combine(Path.GetTempPath(), "momoka-unc-index-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var sourceRoot = Path.Combine(root, "source");
                Directory.CreateDirectory(sourceRoot);
                var library = new LocalMediaLibrary(index);
                await using var monitor = new LocalMediaMonitor(library, (_, _) => Task.FromResult(Movie()), Image);
                await monitor.StartAsync(new("one", sourceRoot, "", LocalMediaLinkMode.Rename)
                {
                    IncludeExisting = false,
                    SettleTime = TimeSpan.FromMilliseconds(150),
                    PollInterval = TimeSpan.FromMilliseconds(100),
                    RescanInterval = TimeSpan.FromSeconds(1)
                });
                File.WriteAllText(Path.Combine(sourceRoot, "incoming.mkv"), "network-video");
                await Until(() => library.Entries.Count == 1);
                Assert.True(library.Entries.Single().IsInLibrary);
                Assert.True(library.Entries.Single().TargetPath.StartsWith(@"\\localhost\", StringComparison.OrdinalIgnoreCase));
                Assert.True(File.Exists(library.Entries.Single().TargetPath));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                if (File.Exists(index)) File.Delete(index);
            }
        }));

        Test("本地监控：配置切换等待旧识别退出且不向旧目标写", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("film.mkv");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<LocalMediaMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var library = new LocalMediaLibrary(files.Index);
            await using var monitor = new LocalMediaMonitor(library, (_, _) => { if (++calls == 1) { started.SetResult(); return release.Task; } return Task.FromResult(Movie()); }, Image);
            await monitor.StartAsync(files.Options());
            await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var newTarget = files.Target + "-new";
            var update = monitor.UpdateAsync(files.Options() with { TargetRoot = newTarget, ProviderKey = "other" });
            release.SetResult(Movie());
            await update;
            await Until(() => library.Entries.Count == 1);
            Assert.False(Directory.Exists(files.Target));
            Assert.True(library.Entries.Single().TargetPath.StartsWith(newTarget, StringComparison.Ordinal));
        }));

        Test("本地媒体库：实际视频成功但NFO失败不能称入库", () => Run(async () =>
        {
            using var files = new Files();
            var source = files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            var plan = LocalMediaOrganizer.Prepare(source, files.Target, Movie() with { Overview = "invalid\u0001xml" }, LocalMediaLinkMode.HardLink);
            Assert.False((await library.ExecuteAsync("one", plan, Image, CancellationToken.None)).Success);
            Assert.True(File.Exists(plan.TargetPath));
            Assert.False(library.Entries.Single().IsInLibrary);
            Assert.False(library.Entries.Single().Complete);
            var loaded = new LocalMediaLibrary(files.Index);
            Assert.False(loaded.Entries.Single().IsInLibrary);
        }));

        Test("本地监控：同一源在不同媒体库不借用执行状态", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            await using var one = new LocalMediaMonitor(library, (_, _) => Task.FromResult(Movie()), Image);
            await using var two = new LocalMediaMonitor(library, (_, _) => Task.FromResult(Movie()), Image);
            await one.StartAsync(files.Options());
            await two.StartAsync(files.Options() with { LibraryId = "two", TargetRoot = files.Target + "-two" });
            await Until(() => library.Entries.Count == 2);
            Assert.Equal(2, library.Entries.Select(entry => entry.LibraryId).Distinct().Count());
            Assert.Equal(2, library.Entries.Select(entry => entry.Id).Distinct().Count());
            Assert.True(library.Entries.All(entry => entry.IsInLibrary));
        }));

        Test("本地监控：识别身份不可信只有持久待校正且重启不重读", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            await using (var monitor = new LocalMediaMonitor(library, (_, _) => Task.FromResult(Movie() with { MediaId = "" }), Image))
            {
                await monitor.StartAsync(files.Options());
                await Until(() => library.Issues.Count == 1);
                Assert.False(library.Issues.Single().WriteAttempted);
                Assert.False(Directory.Exists(files.Target));
            }
            var calls = 0;
            await using var restarted = new LocalMediaMonitor(new(files.Index), (_, _) => { calls++; return Task.FromResult(Movie()); }, Image);
            await restarted.StartAsync(files.Options());
            await Task.Delay(400);
            Assert.Equal(0, calls);
        }));

        Test("本地监控：断开目录后刷新恢复扫描但不跟随链接目录", () => Run(async () =>
        {
            using var files = new Files();
            var library = new LocalMediaLibrary(files.Index);
            await using var monitor = new LocalMediaMonitor(library, (_, _) => Task.FromResult(Movie()), Image);
            await monitor.StartAsync(files.Options());
            var offline = files.Source + "-offline";
            Directory.Move(files.Source, offline);
            monitor.Refresh();
            await Until(() => monitor.Status.Message.Contains("不可用", StringComparison.Ordinal));
            Directory.Move(offline, files.Source);
            files.Video("restored.mkv");
            monitor.Refresh();
            await Until(() => library.Entries.Count == 1);
            Assert.True(library.Entries.Single().IsInLibrary);
        }));

        Test("本地监控：同步Cancel撤销迟到识别无需等待UI队列", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("film.mkv");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<LocalMediaMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
            var library = new LocalMediaLibrary(files.Index);
            await using var monitor = new LocalMediaMonitor(library, (_, _) => { started.SetResult(); return release.Task; }, Image);
            await monitor.StartAsync(files.Options());
            await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var cancel = typeof(LocalMediaMonitor).GetMethod("Cancel");
            Assert.NotNull(cancel, "服务失效必须能同步取消自动写入");
            cancel!.Invoke(monitor, null);
            release.SetResult(Movie());
            await monitor.StopAsync();
            Assert.False(Directory.Exists(files.Target));
            Assert.Equal(0, library.Entries.Count);
        }));

        Test("本地监控：Refresh不能重新开放部分写入", () => Run(async () =>
        {
            using var files = new Files();
            files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            var calls = 0;
            await using var monitor = new LocalMediaMonitor(library, (_, _) => { calls++; return Task.FromResult(Movie() with { PosterUrl = "https://example.invalid/a.jpg" }); },
                (_, _) => Task.FromException<byte[]>(new IOException("图片失败")));
            await monitor.StartAsync(files.Options());
            await Until(() => library.Entries.Count == 1);
            monitor.Refresh();
            await Task.Delay(400);
            Assert.Equal(1, calls);
            Assert.False(library.Entries.Single().Complete);
        }));

        Test("本地媒体库：输出快照不可改且Changed订阅失败不影响完成", () => Run(async () =>
        {
            using var files = new Files();
            var source = files.Video("film.mkv");
            var library = new LocalMediaLibrary(files.Index);
            library.Changed += (_, _) => throw new InvalidOperationException("页面已释放");
            var plan = LocalMediaOrganizer.Prepare(source, files.Target, Movie(), LocalMediaLinkMode.HardLink);
            Assert.True((await library.ExecuteAsync("one", plan, Image, CancellationToken.None)).Success);
            Assert.True(library.Entries.Single().IsInLibrary);
            Assert.Throws<NotSupportedException>(() => ((IList<LocalMediaItemResult>)library.Entries.Single().Outputs).Clear());
        }));
    }

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();
    private static LocalMediaMetadata Movie() => new("Film", 2026, LocalMediaType.Movie, "themoviedb", "123");
    private static Task<byte[]> Image(string _, CancellationToken cancellation) => Task.FromResult(new byte[] { 255, 216, 255, 224, 0, 2, 255, 217 });
    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(4)) throw new AssertionException("后台监控在4秒内未达到期望状态");
            await Task.Delay(25);
        }
    }

    private sealed class Files : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Momoka-monitor-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(_root, "source");
        public string Target => Path.Combine(_root, "target");
        public string Index => Path.Combine(_root, "index.json");
        public Files() => Directory.CreateDirectory(Source);
        public LocalMediaSource Video(string name)
        {
            File.WriteAllText(Path.Combine(Source, name), "video");
            return LocalMediaOrganizer.Scan(Source, true, CancellationToken.None).Single(item => Path.GetFileName(item.Path) == name);
        }
        public LocalMediaMonitorOptions Options() => new("one", Source, Target, LocalMediaLinkMode.HardLink)
        { SettleTime = TimeSpan.FromMilliseconds(150), PollInterval = TimeSpan.FromMilliseconds(50), RescanInterval = TimeSpan.FromSeconds(10) };
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
