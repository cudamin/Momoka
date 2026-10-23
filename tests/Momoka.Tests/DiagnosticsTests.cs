using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;
#if DIAGNOSTICS_VM_FIXTURE
using System.Collections.Specialized;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
#endif

namespace Momoka.Tests;

/// <summary>
/// Covers the diagnostics ring buffer, atomic snapshot watermarks and notification handoff without UI.
/// The optional source-linked VM fixture exercises the production log/filter/copy code with a controlled
/// dispatcher and fake WinUI/platform boundaries; it does not replace an isolated real-window check.
/// <para>
/// The file sink is here for a different reason: it holds a handle open for the life of the process, and
/// the two things that buys — the tail readable before the writer closes, a new file after midnight —
/// are invisible until the day someone needs the log of a crash.
/// </para>
/// </summary>
internal static class DiagnosticsTests
{
    private const string Category = "测试";

    public static void Register()
    {
        RegisterRingBuffer();
        RegisterWrittenEvent();
        RegisterSnapshotHandoff();
#if DIAGNOSTICS_VM_FIXTURE
        RegisterVmHandoff();
#endif
        RegisterEntryText();
        RegisterFileSink();
    }

#if DIAGNOSTICS_VM_FIXTURE
    // Entry point only in the isolated fixture, never in the ordinary test host or product app.
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("R061 真实诊断 VM 源链接离线夹具（无 WinUI 渲染/应用/登录/播放）");
        Register();
        return TestHarness.Run();
    }

    private static void RegisterVmHandoff()
    {
        Test("R061 VM：挂通知后刷新，放通知不再追加快照已有日志", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(8));
            fixture.RefreshLog();
            fixture.Sink.Write(Entry("排队的一条"));
            Assert.Equal(1, fixture.Dispatcher.Count);
            fixture.RefreshLog();
            Assert.Equal(1, fixture.ViewModel.Rows.Count);
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
            Assert.Equal("1 条内存日志", fixture.ViewModel.LogCount);
        });

        Test("R061 VM：刷新交接之后的新通知继续正常追加", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(8));
            fixture.RefreshLog();
            fixture.Sink.Write(Entry("刷新中的旧通知"));
            fixture.RefreshLog();
            fixture.Dispatcher.Drain();
            fixture.Sink.Write(Entry("刷新之后的新通知"));
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
            Assert.Equal(2, fixture.ViewModel.Rows.Count);
        });

        Test("R061 VM：通知反序不丢日志，显示与复制保持缓冲顺序", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(8));
            fixture.RefreshLog();
            fixture.Sink.Write(Entry("先写"));
            fixture.Sink.Write(Entry("后写"));
            fixture.Dispatcher.Drain(reverse: true);
            fixture.AssertMatchesBuffer();
            Assert.Equal(2, fixture.ViewModel.Rows.Count);
        });

        Test("R061 VM：完全相同的日志写两次仍保留两条", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(8));
            fixture.RefreshLog();
            var entry = new LogEntry(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.FromHours(8)),
                LogLevel.Info, "相同分类", "完全一样", "相同详情");
            fixture.Sink.Write(entry);
            fixture.Sink.Write(entry);
            fixture.Dispatcher.Drain(reverse: true);
            fixture.AssertMatchesBuffer();
            Assert.Equal(2, fixture.ViewModel.Rows.Count);
        });

        Test("R061 VM：首读快照和先前排队通知交接不重复", () =>
        {
            var sink = new RingBufferLogSink(8);
            sink.Write(Entry("打开前的旧日志"));
            using var fixture = new VmLogFixture(sink);
            sink.Write(Entry("首读前排队"));
            fixture.RefreshLog();
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：通知先于显式首读执行也会补齐缓冲旧日志", () =>
        {
            var sink = new RingBufferLogSink(8);
            sink.Write(Entry("打开前的旧日志"));
            using var fixture = new VmLogFixture(sink);
            sink.Write(Entry("先执行的通知"));
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
            fixture.RefreshLog();
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：容量淘汰同步筛选、空态、分类和复制", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(2));
            fixture.RefreshLog();
            fixture.ViewModel.SelectedLevel = 3;
            fixture.Sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Error, "将淘汰", "旧错误", null));
            fixture.Dispatcher.Drain();
            fixture.Sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "保留", "新信息一", null));
            fixture.Dispatcher.Drain();
            fixture.ViewModel.SelectedCategory = "将淘汰";
            fixture.Sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "保留", "新信息二", null));
            fixture.Dispatcher.Drain();
            Assert.Equal(0, fixture.ViewModel.Rows.Count);
            Assert.True(fixture.ViewModel.ShowEmptyLogs);
            Assert.Equal("0 / 2 条内存日志", fixture.ViewModel.LogCount);
            Assert.False(fixture.ViewModel.Categories.Contains("将淘汰"));
            Assert.Equal("全部", fixture.ViewModel.SelectedCategory);
            fixture.ViewModel.SelectedLevel = 0;
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：多线程写入、刷新与逆序放通知仍等于有界缓冲", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(37));
            fixture.RefreshLog();
            Parallel.For(0, 240, index => fixture.Sink.Write(Entry($"并发 {index % 5}")));
            Assert.Equal(0, fixture.ViewModel.Rows.Count, "Post 不能直接在写入线程改 Rows");
            fixture.RefreshLog();
            fixture.Dispatcher.Drain(reverse: true);
            fixture.AssertMatchesBuffer();
            Assert.Equal(37, fixture.ViewModel.Rows.Count);
        });

        Test("R061 VM：排队超过容量只追加缓冲保留尾部，筛选复制不留旧条", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(3));
            fixture.RefreshLog();
            fixture.Sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Error, "淘汰分类", "旧错误", null));
            fixture.Dispatcher.Drain();
            fixture.ViewModel.SelectedLevel = 3;
            for (var index = 1; index <= 20; index++)
                fixture.Sink.Write(new LogEntry(DateTimeOffset.Now, index % 2 == 0 ? LogLevel.Error : LogLevel.Info,
                    "保留分类", $"排队 {index}", index % 2 == 0 ? "详情" : null));
            fixture.Dispatcher.Drain(reverse: true);
            fixture.AssertMatchesBuffer();
            Assert.Equal(2, fixture.ViewModel.Rows.Count);
            Assert.DoesNotContain("旧错误", fixture.Clipboard.Text);
            fixture.ViewModel.SelectedCategory = "保留分类";
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：受控多线程通知真反序时首条仍补齐，迟到通知不重复", () =>
        {
            var sink = new RingBufferLogSink(8);
            using var firstHeld = new ManualResetEventSlim();
            using var releaseFirst = new ManualResetEventSlim();
            sink.Written += entry =>
            {
                if (entry.Message != "先写后通知") return;
                firstHeld.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            };
            using var fixture = new VmLogFixture(sink);
            fixture.RefreshLog();
            var first = new Thread(() => sink.Write(Entry("先写后通知"))) { IsBackground = true };
            var second = new Thread(() => sink.Write(Entry("后写先通知"))) { IsBackground = true };
            first.Start();
            try
            {
                Assert.True(firstHeld.Wait(TimeSpan.FromSeconds(5)));
                second.Start();
                Assert.True(second.Join(TimeSpan.FromSeconds(5)), "第一订阅者不能阻塞第二写入");
                Assert.Equal(1, fixture.Dispatcher.Count, "仅第二写入通知先进入 UI 队列");
                fixture.Dispatcher.Drain();
                fixture.AssertMatchesBuffer();
                Assert.Equal(2, fixture.ViewModel.Rows.Count, "第一写入的通知未到也不能丢第一条");
                fixture.RefreshLog();
            }
            finally
            {
                releaseFirst.Set();
                first.Join(TimeSpan.FromSeconds(5));
                if ((second.ThreadState & ThreadState.Unstarted) == 0) second.Join(TimeSpan.FromSeconds(5));
            }
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：坏订阅者不挡住后面的诊断通知", () =>
        {
            var sink = new RingBufferLogSink(8);
            sink.Written += _ => throw new InvalidOperationException("假失败订阅者");
            using var fixture = new VmLogFixture(sink);
            fixture.RefreshLog();
            sink.Write(Entry("失败后仍通知"));
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：常规通知增量更新，不重建整个 Rows", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(8));
            fixture.RefreshLog();
            var rows = fixture.ViewModel.Rows;
            var resets = 0;
            rows.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset) resets++;
            };
            fixture.Sink.Write(Entry("保留原行"));
            fixture.Dispatcher.Drain();
            var first = rows[0];
            fixture.Sink.Write(Entry("新增一行"));
            fixture.Dispatcher.Drain();
            Assert.True(ReferenceEquals(rows, fixture.ViewModel.Rows));
            Assert.True(ReferenceEquals(first, rows[1]));
            Assert.Equal(0, resets);
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：换缓冲后不把旧排队通知追加到新来源", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(8));
            fixture.RefreshLog();
            fixture.Sink.Write(Entry("旧来源排队"));
            var next = new RingBufferLogSink(8);
            next.Write(Entry("新来源"));
            fixture.Attach(next);
            fixture.RefreshLog();
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
        });

        Test("R061 VM：暂停与离页仍拒绝排队回调，恢复后补齐未读日志", () =>
        {
            using var fixture = new VmLogFixture(new RingBufferLogSink(8));
            fixture.RefreshLog();
            fixture.Sink.Write(Entry("暂停前排队"));
            fixture.ViewModel.Following = false;
            fixture.Dispatcher.Drain();
            Assert.Equal(0, fixture.ViewModel.Rows.Count);
            fixture.Sink.Write(Entry("暂停期间"));
            Assert.Equal(0, fixture.Dispatcher.Count, "关闭跟随不应为每条日志排队");
            fixture.Dispatcher.Drain();
            fixture.ViewModel.Following = true;
            fixture.Sink.Write(Entry("恢复之后"));
            fixture.Dispatcher.Drain();
            fixture.AssertMatchesBuffer();
            fixture.Sink.Write(Entry("离页前排队"));
            fixture.ViewModel.Detach();
            fixture.Dispatcher.Drain();
            Assert.Equal(3, fixture.ViewModel.Rows.Count);
        });
    }

    private sealed class VmLogFixture : IDisposable
    {
        private readonly AppSettings _settings = new();
        private readonly PlaybackService _playback;

        public VmLogFixture(RingBufferLogSink sink)
        {
            // None of these services are asked to read configuration, sign in or start a backend.
            _playback = new PlaybackService(null!, _settings,
                () => throw new InvalidOperationException("夹具禁止启动播放"), null!, allowPlayback: false);
            Attach(sink);
        }

        public RingBufferLogSink Sink { get; private set; } = null!;
        public DiagnosticsViewModel ViewModel { get; } = new();
        public ControlledLogDispatcher Dispatcher { get; } = new();
        public MemoryLogClipboard Clipboard { get; } = new();

        public void Attach(RingBufferLogSink sink)
        {
            Sink = sink;
            ViewModel.Attach(null!, new SettingsService(null!, _settings), null!, null!, _playback,
                new AppPaths(Path.Combine(Path.GetTempPath(), "r061-unused-app-path")), Dispatcher,
                Clipboard, null!, sink);
        }

        public void RefreshLog() => typeof(DiagnosticsViewModel)
            .GetMethod("ReadLog", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ViewModel, null);

        public void AssertMatchesBuffer()
        {
            var snapshot = Sink.Snapshot();
            var minimum = ViewModel.SelectedLevel switch
            {
                1 => LogLevel.Info,
                2 => LogLevel.Warn,
                3 => LogLevel.Error,
                _ => LogLevel.Debug
            };
            var expected = snapshot.Where(entry => entry.Level >= minimum
                && (ViewModel.SelectedCategory is null or "全部" || entry.Category == ViewModel.SelectedCategory)).ToList();
            Assert.True(expected.AsEnumerable().Reverse().SequenceEqual(ViewModel.Rows.Select(row => row.Entry)),
                "真实 VM 的 Rows 应是缓冲快照筛选后倒序，不能丢条、重复或按事件到达次序排列");
            Assert.Equal(expected.Count == 0, ViewModel.ShowEmptyLogs);
            Assert.Equal(expected.Count == snapshot.Count ? $"{snapshot.Count} 条内存日志"
                : $"{expected.Count} / {snapshot.Count} 条内存日志", ViewModel.LogCount);
            var categories = new[] { "全部" }.Concat(snapshot.Select(entry => entry.Category)
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.CurrentCulture));
            Assert.True(categories.SequenceEqual(ViewModel.Categories, StringComparer.Ordinal));
            if (expected.Count == 0) return;
            ViewModel.CopyCommand.Execute(null);
            Assert.Equal(string.Join(Environment.NewLine, expected.Select(entry => entry.ToString())), Clipboard.Text,
                "复制应按缓冲写入顺序并保留筛选，使用假剪贴板");
        }

        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class ControlledLogDispatcher : IUiDispatcher
    {
        private readonly List<Action> _pending = [];
        private readonly object _gate = new();

        public int Count
        {
            get { lock (_gate) return _pending.Count; }
        }

        public bool Post(Action work)
        {
            lock (_gate) _pending.Add(work);
            return true;
        }

        public void Run(Action work) => work();

        public void Drain(bool reverse = false)
        {
            while (true)
            {
                Action work;
                lock (_gate)
                {
                    if (_pending.Count == 0) return;
                    var at = reverse ? _pending.Count - 1 : 0;
                    work = _pending[at];
                    _pending.RemoveAt(at);
                }
                work();
            }
        }
    }

    private sealed class MemoryLogClipboard : IClipboard
    {
        public string? Text { get; private set; }
        public void SetText(string text) => Text = text;
    }
#endif

    // ---- 环形缓冲 --------------------------------------------------------------

    private static void RegisterRingBuffer()
    {
        Test("内存日志：快照按写入顺序返回，最旧的在前", () =>
        {
            var sink = new RingBufferLogSink(10);
            for (var index = 1; index <= 4; index++) sink.Write(Entry($"第 {index} 条"));

            var snapshot = sink.Snapshot();
            Assert.Equal(4, snapshot.Count);
            Assert.Equal("第 1 条", snapshot[0].Message);
            Assert.Equal("第 4 条", snapshot[3].Message);
        });

        Test("内存日志：空缓冲的快照是空的，不是 null", () =>
        {
            var snapshot = new RingBufferLogSink(10).Snapshot();
            Assert.NotNull(snapshot);
            Assert.Equal(0, snapshot.Count);
        });

        Test("内存日志：写满之后丢掉最旧的，顺序不乱", () =>
        {
            // Three past capacity, so the wrap-around arithmetic in Snapshot is exercised rather than
            // just the 「exactly full」 case that happens to work with start == 0.
            var sink = new RingBufferLogSink(5);
            for (var index = 1; index <= 8; index++) sink.Write(Entry($"第 {index} 条"));

            var snapshot = sink.Snapshot();
            Assert.Equal(5, snapshot.Count);
            Assert.Equal("第 4 条", snapshot[0].Message);
            Assert.Equal("第 8 条", snapshot[4].Message);
        });

        Test("内存日志：容量对外可读，页面靠它决定自己留多少行", () =>
        {
            // The diagnostics page trims its own list to this number; if the property stopped agreeing
            // with the buffer, the page would grow without limit for as long as it stayed open.
            Assert.Equal(500, new RingBufferLogSink().Capacity, "默认容量");
            Assert.Equal(7, new RingBufferLogSink(7).Capacity);
        });

        Test("内存日志：容量不能小于 1", () =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RingBufferLogSink(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RingBufferLogSink(-1));
        });
    }

    // ---- 实时事件 --------------------------------------------------------------

    private static void RegisterWrittenEvent()
    {
        Test("实时日志：每写一条就通知一次，内容一致", () =>
        {
            var sink = new RingBufferLogSink(10);
            var seen = new List<LogEntry>();
            sink.Written += entry => seen.Add(entry);

            sink.Write(Entry("一"));
            sink.Write(Entry("二"));

            Assert.Equal(2, seen.Count);
            Assert.Equal("一", seen[0].Message);
            Assert.Equal("二", seen[1].Message);
        });

        Test("实时日志：通知时缓冲里已经有这条了", () =>
        {
            // The page appends on this event and re-snapshots on 刷新; if the event fired before the entry
            // was committed, the two would disagree and a refresh would appear to lose the newest line.
            var sink = new RingBufferLogSink(10);
            var countAtNotify = -1;
            sink.Written += _ => countAtNotify = sink.Snapshot().Count;

            sink.Write(Entry("一"));

            Assert.Equal(1, countAtNotify, "事件触发时快照里应该已经包含这条");
        });

        Test("实时日志：取消订阅之后不再收到通知", () =>
        {
            // What the page does on navigating away. A handler left attached would keep a dead page alive
            // and touch its bound collections from a background thread.
            var sink = new RingBufferLogSink(10);
            var count = 0;
            void Handler(LogEntry entry) => count++;

            sink.Written += Handler;
            sink.Write(Entry("一"));
            sink.Written -= Handler;
            sink.Write(Entry("二"));

            Assert.Equal(1, count, "退订后写入的那条不该再通知");
        });

        Test("实时日志：订阅者抛异常不会带走写入方", () =>
        {
            // Deliberate in the sink: a throwing handler must not turn logging into a crash, and must not
            // stop the entry being stored or the next handler being called.
            var sink = new RingBufferLogSink(10);
            var reached = false;
            sink.Written += _ => throw new InvalidOperationException("订阅者炸了");
            sink.Written += _ => reached = true;

            sink.Write(Entry("一"));

            Assert.Equal(1, sink.Snapshot().Count, "抛异常也要把日志存下来");
            Assert.True(reached, "前一个订阅者抛异常不应挡住后一个");
        });

        Test("实时日志：没有订阅者时写入照样正常", () =>
        {
            var sink = new RingBufferLogSink(10);
            sink.Write(Entry("一"));
            Assert.Equal(1, sink.Snapshot().Count);
        });
    }

    // ---- 快照水位交接（普通 Core 测试，不冒充真实 UI）----------------------------

    private static void RegisterSnapshotHandoff()
    {
        Test("日志水位：空快照为零，完整快照与增量水位来自同一写入", () =>
        {
            var sink = new RingBufferLogSink(5);
            var empty = sink.SnapshotAfter(0);
            Assert.Equal(0L, empty.Watermark);
            Assert.Equal(0, empty.Entries.Count);
            sink.Write(Entry("一"));
            sink.Write(Entry("二"));
            var snapshot = sink.SnapshotAfter(0);
            Assert.Equal(2L, snapshot.Watermark);
            Assert.True(sink.Snapshot().SequenceEqual(snapshot.Entries));
            Assert.Equal(0, sink.SnapshotAfter(snapshot.Watermark).Entries.Count);
            sink.Write(Entry("三"));
            var tail = sink.SnapshotAfter(snapshot.Watermark);
            Assert.Equal(3L, tail.Watermark);
            Assert.Equal(1, tail.Entries.Count);
            Assert.Equal("三", tail.Entries[0].Message);
            Assert.Equal(2, snapshot.Entries.Count, "后续写入不应改变已取快照");
        });

        Test("日志水位：挂通知、刷新快照、放通知只消费一次，后续新条正常", () =>
        {
            var sink = new RingBufferLogSink(5);
            var displayed = new List<LogEntry>();
            var pending = new Queue<Action>();
            var watermark = 0L;
            sink.Written += _ => pending.Enqueue(() =>
            {
                var next = sink.SnapshotAfter(watermark);
                displayed.AddRange(next.Entries);
                watermark = next.Watermark;
            });
            sink.Write(Entry("快照已包含"));
            var refresh = sink.SnapshotAfter(0);
            displayed.AddRange(refresh.Entries);
            watermark = refresh.Watermark;
            pending.Dequeue()();
            Assert.Equal(1, displayed.Count);
            sink.Write(Entry("之后的新条"));
            pending.Dequeue()();
            Assert.True(sink.Snapshot().SequenceEqual(displayed));
        });

        Test("日志水位：完全相同的文本、详情和时间戳仍是不同写入", () =>
        {
            var sink = new RingBufferLogSink(5);
            var entry = new LogEntry(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.FromHours(8)),
                LogLevel.Info, "相同", "相同", "相同");
            sink.Write(entry);
            var before = sink.SnapshotAfter(0);
            sink.Write(entry);
            var after = sink.SnapshotAfter(before.Watermark);
            Assert.Equal(1L, before.Watermark);
            Assert.Equal(2L, after.Watermark);
            Assert.Equal(1, after.Entries.Count);
            Assert.Equal(entry, after.Entries[0]);
            Assert.Equal(2, sink.Snapshot().Count);
        });

        Test("日志水位：落后超过容量只给保留的尾部，不找回淘汰条目", () =>
        {
            var sink = new RingBufferLogSink(3);
            sink.Write(Entry("已读的一条"));
            var before = sink.SnapshotAfter(0);
            for (var index = 2; index <= 9; index++) sink.Write(Entry($"第 {index} 条"));
            var tail = sink.SnapshotAfter(before.Watermark);
            Assert.Equal(9L, tail.Watermark);
            Assert.Equal(3, tail.Entries.Count);
            Assert.Equal("第 7 条", tail.Entries[0].Message);
            Assert.Equal("第 9 条", tail.Entries[2].Message);
            Assert.True(sink.Snapshot().SequenceEqual(tail.Entries));
            Assert.Equal(0, sink.SnapshotAfter(tail.Watermark).Entries.Count);
        });

        Test("日志水位：容量一在多次环绕之后仍能可靠增量读取", () =>
        {
            var sink = new RingBufferLogSink(1);
            for (var index = 1; index <= 20; index++) sink.Write(Entry($"第 {index} 条"));
            var tail = sink.SnapshotAfter(1);
            Assert.Equal(20L, tail.Watermark);
            Assert.Equal(1, tail.Entries.Count);
            Assert.Equal("第 20 条", tail.Entries[0].Message);
            Assert.Equal(0, sink.SnapshotAfter(20).Entries.Count);
        });

        Test("日志水位：负水位被拒绝，未来水位不会产生旧条目", () =>
        {
            var sink = new RingBufferLogSink(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => sink.SnapshotAfter(-1));
            sink.Write(Entry("一"));
            var future = sink.SnapshotAfter(long.MaxValue);
            Assert.Equal(1L, future.Watermark);
            Assert.Equal(0, future.Entries.Count);
        });

        Test("日志水位：订阅者阻塞不持写入锁，反序通知仍能补齐全部写入", () =>
        {
            var sink = new RingBufferLogSink(5);
            using var firstHeld = new ManualResetEventSlim();
            using var releaseFirst = new ManualResetEventSlim();
            var order = new List<string>();
            var orderGate = new object();
            var displayed = new List<LogEntry>();
            var watermark = 0L;
            var consumerGate = new object();
            sink.Written += entry =>
            {
                if (entry.Message != "先写") return;
                firstHeld.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            };
            sink.Written += entry =>
            {
                lock (orderGate) order.Add(entry.Message);
                lock (consumerGate)
                {
                    var tail = sink.SnapshotAfter(watermark);
                    displayed.AddRange(tail.Entries);
                    watermark = tail.Watermark;
                }
            };
            var first = new Thread(() => sink.Write(Entry("先写"))) { IsBackground = true };
            var second = new Thread(() => sink.Write(Entry("后写"))) { IsBackground = true };
            first.Start();
            try
            {
                Assert.True(firstHeld.Wait(TimeSpan.FromSeconds(5)), "第一通知应被受控地挂起");
                second.Start();
                Assert.True(second.Join(TimeSpan.FromSeconds(5)), "任意订阅者阻塞不能卡住第二写入");
                Assert.Equal("后写", order[0], "确认通知真正反序，不靠概率调度");
            }
            finally
            {
                releaseFirst.Set();
                first.Join(TimeSpan.FromSeconds(5));
                if ((second.ThreadState & ThreadState.Unstarted) == 0) second.Join(TimeSpan.FromSeconds(5));
            }
            Assert.Equal(2, order.Count);
            Assert.True(sink.Snapshot().SequenceEqual(displayed));
            Assert.Equal(2L, watermark);
        });

        Test("日志水位：多线程写入时快照内容与水位仍是原子一致", () =>
        {
            const int capacity = 31;
            const int writes = 600;
            var sink = new RingBufferLogSink(capacity);
            var observed = new System.Collections.Concurrent.ConcurrentBag<LogSnapshot>();
            sink.Written += _ => observed.Add(sink.SnapshotAfter(0));
            Parallel.For(0, writes, index => sink.Write(Entry($"并发 {index % 4}")));
            Assert.Equal(writes, observed.Count);
            foreach (var snapshot in observed)
            {
                Assert.Equal((int)Math.Min(capacity, snapshot.Watermark), snapshot.Entries.Count,
                    "快照与水位不能分两次读取");
                Assert.True(snapshot.Watermark >= 1 && snapshot.Watermark <= writes);
                Assert.True(sink.SnapshotAfter(snapshot.Watermark).Entries.Count <= capacity);
            }
            var final = sink.SnapshotAfter(0);
            Assert.Equal((long)writes, final.Watermark);
            Assert.Equal(capacity, final.Entries.Count);
        });
    }

    // ---- 单条日志的文本 --------------------------------------------------------

    private static void RegisterEntryText()
    {
        Test("日志文本：级别补齐到五格，复制出来能对齐", () =>
        {
            var text = new LogEntry(
                new DateTimeOffset(2026, 8, 23, 7, 30, 15, 250, TimeSpan.FromHours(8)),
                LogLevel.Warn,
                "playback",
                "开始播放",
                null).ToString();

            Assert.Contains("[WARN ] playback: 开始播放", text);
            Assert.DoesNotContain(Environment.NewLine, text, "没有 Detail 就不该多出一行");
        });

        Test("日志文本：有 Detail 时另起一行", () =>
        {
            var text = new LogEntry(DateTimeOffset.Now, LogLevel.Error, "mpv", "启动失败", "找不到 libmpv-2.dll")
                .ToString();

            Assert.Contains("[ERROR] mpv: 启动失败", text);
            Assert.Contains(Environment.NewLine + "找不到 libmpv-2.dll", text);
        });
    }

    // ---- 落盘的日志 ------------------------------------------------------------

    private static void RegisterFileSink()
    {
        Test("磁盘日志：写进当天那个文件，一行一条", () => InTempDirectory(directory =>
        {
            var now = DateTimeOffset.Now;
            using (var sink = new FileLogSink(directory))
            {
                sink.Write(Entry("第一条", LogLevel.Info, now));
                sink.Write(Entry("第二条", LogLevel.Warn, now));
            }

            var lines = File.ReadAllLines(LogPath(directory, now));
            Assert.Equal(2, lines.Length);
            Assert.Contains("第一条", lines[0]);
            Assert.Contains("第二条", lines[1]);
        }));

        Test("磁盘日志：句柄还开着的时候就已经读得到", () => InTempDirectory(directory =>
        {
            var now = DateTimeOffset.Now;
            using var sink = new FileLogSink(directory);
            sink.Write(Entry("崩之前最后一句", LogLevel.Error, now));

            // 这一条钉的是这次改动的要害：句柄常开省掉了每行一次的开关文件，但日志的用处正在于进程被
            // 它自己在查的那个毛病带走时最后那几行还在盘上。所以必须是「写完立刻可读」，不能等关闭。
            Assert.Contains("崩之前最后一句", ReadWhileOpen(LogPath(directory, now)));
        }));

        Test("磁盘日志：低于门槛的那些不落盘", () => InTempDirectory(directory =>
        {
            var now = DateTimeOffset.Now;
            using (var sink = new FileLogSink(directory, LogLevel.Warn))
            {
                sink.Write(Entry("啰嗦话", LogLevel.Debug, now));
                sink.Write(Entry("平常话", LogLevel.Info, now));
                sink.Write(Entry("要紧话", LogLevel.Warn, now));
            }

            var text = File.ReadAllText(LogPath(directory, now));
            Assert.Contains("要紧话", text);
            Assert.DoesNotContain("啰嗦话", text);
            Assert.DoesNotContain("平常话", text);
        }));

        Test("磁盘日志：跨过午夜就换一个文件", () => InTempDirectory(directory =>
        {
            var now = DateTimeOffset.Now;
            var yesterday = now.AddDays(-1);
            using (var sink = new FileLogSink(directory))
            {
                sink.Write(Entry("昨天的", LogLevel.Info, yesterday));
                sink.Write(Entry("今天的", LogLevel.Info, now));
            }

            // 一直开着一个句柄的代价就是这个：换天时要真的换文件。写串了的话，通宵跑的那一遍会把第二天
            // 全部记进前一天的文件里。
            Assert.Contains("昨天的", File.ReadAllText(LogPath(directory, yesterday)));
            Assert.Contains("今天的", File.ReadAllText(LogPath(directory, now)));
        }));

        Test("磁盘日志：过期的旧日志开机时清掉", () => InTempDirectory(directory =>
        {
            Directory.CreateDirectory(directory);
            var stale = Path.Combine(directory, "app-20250101.log");
            var fresh = Path.Combine(directory, "app-20260830.log");
            File.WriteAllText(stale, "很久以前");
            File.WriteAllText(fresh, "前几天");
            File.SetLastWriteTime(stale, DateTime.Now.AddDays(-30));
            File.SetLastWriteTime(fresh, DateTime.Now.AddDays(-2));

            using var sink = new FileLogSink(directory, LogLevel.Debug, retainDays: 14);

            Assert.False(File.Exists(stale), "过了保留期的该删");
            Assert.True(File.Exists(fresh), "保留期内的不能连坐");
        }));
    }

    private static LogEntry Entry(string message) =>
        new(DateTimeOffset.Now, LogLevel.Info, Category, message, null);

    private static LogEntry Entry(string message, LogLevel level, DateTimeOffset when) =>
        new(when, level, Category, message, null);

    /// <summary>
    /// Runs a case against a directory of its own and takes it away afterwards. Every sink inside has to
    /// be disposed before the body returns, or the directory cannot be removed on Windows.
    /// </summary>
    private static void InTempDirectory(Action<string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Momoka-日志-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            body(directory);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string LogPath(string directory, DateTimeOffset when) =>
        Path.Combine(directory, $"app-{when.LocalDateTime:yyyyMMdd}.log");

    /// <summary>
    /// Reads a file the sink still has open. <see cref="FileShare.ReadWrite"/> is not optional here: a
    /// plain <see cref="File.ReadAllText"/> asks that nobody else be writing, and the writer inside is,
    /// so it would fail with a sharing violation. Anything that tails this log needs the same.
    /// </summary>
    private static string ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
