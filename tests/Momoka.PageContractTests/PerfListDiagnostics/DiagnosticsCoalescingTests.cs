using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.CompilerServices;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DiagnosticsCoalescingTests
{
    internal static void Register()
    {
        foreach (var initialized in new[] { false, true })
            Test("PERF-10 实际VM：万条1唤醒／零空回调／容量水位" + (initialized ? "增量" : "首读"), () =>
            {
                using var fixture = new LogFixture(1_000, initialized);
                var rows = fixture.Vm.Rows;
                for (var i = 0; i < 10_000; i++) fixture.Sink.Write(Entry(i));
                Assert.Equal(1, fixture.Queue.Count);
                Assert.Equal(1, fixture.Queue.Posts);
                fixture.Queue.DrainOne();
                fixture.Matches();
                Assert.Equal(0, fixture.Queue.Count);
                Assert.Equal(1_000, fixture.Vm.Rows.Count);
                Assert.Equal(10_000L, fixture.Watermark);
                Assert.Equal(0, fixture.Sink.SnapshotAfter(fixture.Watermark).Entries.Count);
                Assert.True(ReferenceEquals(rows, fixture.Vm.Rows));
                var oldRow = rows[0];
                fixture.Sink.Write(Entry(10_000));
                fixture.Queue.DrainOne();
                Assert.True(ReferenceEquals(oldRow, rows[1]), "普通增量保留旧行对象");
                fixture.Matches();
                Console.WriteLine($"    实际DiagnosticsViewModel：10000条 -> 1回调，首处理后待执行 {fixture.Queue.Count}，保留1000条");
            });

        Test("PERF-10 实际VM：八写线程洪峰仍只有一个待执行回调", () =>
        {
            using var fixture = new LogFixture(400);
            Parallel.For(0, 8, worker =>
            {
                for (var i = 0; i < 1_500; i++) fixture.Sink.Write(Entry(worker * 1_500 + i));
            });
            Assert.Equal(1, fixture.Queue.Count);
            Assert.Equal(1, fixture.Queue.Posts);
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(12_000L, fixture.Watermark);
        });

        foreach (var initialized in new[] { false, true })
            Test("PERF-10 实际VM：" + (initialized ? "Append" : "首Snapshot") + "执行中并发到达重查新增尾部", () =>
            {
                using var fixture = new LogFixture(100, initialized);
                var fired = false;
                fixture.Vm.Rows.CollectionChanged += (_, args) =>
                {
                    if (args.Action != NotifyCollectionChangedAction.Add || fired) return;
                    fired = true;
                    Parallel.For(100, 150, i => fixture.Sink.Write(Entry(i)));
                    Assert.Equal(0, fixture.Queue.Count, "执行中写入不再排第二个回调");
                };
                fixture.Sink.Write(Entry(1));
                fixture.Queue.DrainOne();
                Assert.True(fired);
                fixture.Matches();
                Assert.Equal(51L, fixture.Watermark);
                Assert.Equal(0, fixture.Queue.Count);
                Assert.Equal(1, fixture.Queue.Posts);
                fixture.Sink.Write(Entry(151));
                Assert.Equal(1, fixture.Queue.Count, "结束释放后下一条能重新唤醒");
                fixture.Queue.DrainOne();
                fixture.Matches();
            });

        Test("PERF-10 实际VM：队列拒绝及Post抛异常都复位，下次唤醒补齐保留尾部", () =>
        {
            using var fixture = new LogFixture(20);
            fixture.Queue.RejectNext = true;
            fixture.Sink.Write(Entry(1));
            Assert.Equal(0, fixture.Queue.Count);
            fixture.Sink.Write(Entry(2));
            Assert.Equal(1, fixture.Queue.Count);
            fixture.Queue.DrainOne();
            fixture.Matches();
            fixture.Queue.ThrowNext = true;
            fixture.Sink.Write(Entry(3));
            Assert.Equal(0, fixture.Queue.Count);
            fixture.Sink.Write(Entry(4));
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(4L, fixture.Watermark);
        });

        Test("PERF-10 实际VM：Post重入写日志不死锁、不重复排队、拒绝后可重试", () =>
        {
            using var fixture = new LogFixture(20);
            fixture.Queue.DuringPost = () => fixture.Sink.Write(Entry(2));
            fixture.Sink.Write(Entry(1));
            Assert.Equal(1, fixture.Queue.Count);
            fixture.Queue.DrainOne();
            fixture.Matches();
            fixture.Queue.RejectNext = true;
            fixture.Queue.DuringPost = () => fixture.Sink.Write(Entry(4));
            fixture.Sink.Write(Entry(3));
            Assert.Equal(0, fixture.Queue.Count);
            fixture.Sink.Write(Entry(5));
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(5L, fixture.Watermark);
        });

        Test("PERF-10 实际VM：执行中重入Refresh不回退watermark、不重复旧快照", () =>
        {
            using var fixture = new LogFixture(20);
            var refreshed = false;
            fixture.Vm.Rows.CollectionChanged += (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add || refreshed) return;
                refreshed = true;
                fixture.Sink.Write(Entry(9));
                fixture.RefreshLog();
            };
            for (var i = 0; i < 5; i++) fixture.Sink.Write(Entry(i));
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(6L, fixture.Watermark);
            Assert.Equal(0, fixture.Queue.Count);
        });

        Test("PERF-10 实际VM：暂停拒绝旧队列，不逐条排队；恢复只补保留尾部", () =>
        {
            using var fixture = new LogFixture(4);
            fixture.Sink.Write(Entry(1));
            fixture.Vm.Following = false;
            for (var i = 2; i < 102; i++) fixture.Sink.Write(Entry(i));
            Assert.Equal(1, fixture.Queue.Count);
            fixture.Queue.DrainOne();
            Assert.Equal(0, fixture.Vm.Rows.Count);
            Assert.Equal(0L, fixture.Watermark);
            fixture.Vm.Following = true;
            fixture.Sink.Write(Entry(102));
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(102L, fixture.Watermark);
        });

        Test("PERF-10 实际VM：Append途中暂停水位只到已处理项，恢复不丢尾部", () =>
        {
            using var fixture = new LogFixture(20);
            var paused = false;
            fixture.Vm.Rows.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add && !paused)
                {
                    paused = true;
                    fixture.Vm.Following = false;
                }
            };
            for (var i = 1; i <= 5; i++) fixture.Sink.Write(Entry(i));
            fixture.Queue.DrainOne();
            Assert.Equal(1, fixture.Vm.Rows.Count);
            Assert.Equal(1L, fixture.Watermark);
            fixture.Vm.Following = true;
            fixture.Sink.Write(Entry(6));
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(6L, fixture.Watermark);
        });

        Test("PERF-10 实际VM：Detach及同sink重入旧回调不能借用新visit水位", () =>
        {
            using var fixture = new LogFixture(8);
            fixture.Sink.Write(Entry(1));
            fixture.Vm.Detach();
            Assert.Equal(0L, fixture.Watermark);
            fixture.Attach(fixture.Sink);
            fixture.RefreshLog();
            var oldRow = fixture.Vm.Rows.Single();
            fixture.Sink.Write(Entry(2));
            Assert.Equal(2, fixture.Queue.Count);
            fixture.Queue.DrainOne();
            Assert.Equal(1, fixture.Vm.Rows.Count, "旧visit即使sink相同也不能消费新水位");
            Assert.Equal(1L, fixture.Watermark);
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.True(ReferenceEquals(oldRow, fixture.Vm.Rows[1]));
        });

        Test("PERF-10 实际VM：旧sink已捕获但迟到的通知不会唤醒新visit", () =>
        {
            var sink = new RingBufferLogSink(10);
            using var entered = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            sink.Written += _ =>
            {
                entered.Set();
                Assert.True(released.Wait(TimeSpan.FromSeconds(10)));
            };
            using var fixture = new LogFixture(sink);
            var writer = new Thread(() => sink.Write(Entry(1))) { IsBackground = true };
            writer.Start();
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                var next = new RingBufferLogSink(10);
                fixture.Attach(next);
                fixture.RefreshLog();
                released.Set();
                Assert.True(writer.Join(TimeSpan.FromSeconds(10)));
                Assert.Equal(0, fixture.Queue.Count, "旧sink事件委托已捕获但其owner已Stop");
                next.Write(Entry(2));
                Assert.Equal(1, fixture.Queue.Count);
                fixture.Queue.DrainOne();
                fixture.Matches();
            }
            finally { released.Set(); writer.Join(TimeSpan.FromSeconds(10)); }
        });

        Test("PERF-10 实际VM：执行中Detach／Attach新sink拒绝旧快照尾与旧水位", () =>
        {
            using var fixture = new LogFixture(10);
            var changed = false;
            fixture.Vm.Rows.CollectionChanged += (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add || changed) return;
                changed = true;
                var next = new RingBufferLogSink(10);
                next.Write(Entry(20));
                fixture.Attach(next);
                fixture.RefreshLog();
                next.Write(Entry(21));
            };
            for (var i = 0; i < 5; i++) fixture.Sink.Write(Entry(i));
            fixture.Queue.DrainOne();
            Assert.Equal(1L, fixture.Watermark);
            fixture.Queue.Drain();
            fixture.Matches();
            Assert.Equal(2L, fixture.Watermark);
        });

        Test("PERF-10 实际VM：Detach后保留旧队列也能GC回收VM，不等队列drain", () =>
        {
            var queue = new LogQueue();
            var sink = new RingBufferLogSink(100);
            var weak = Abandon(sink, queue);
            Collect();
            Assert.Equal(1, queue.Count);
            Assert.False(weak.IsAlive, "旧回调只能持有弱VM的窄visit owner");
            sink.Write(Entry(20_000));
            Assert.Equal(1, queue.Count);
            queue.Drain();
            GC.KeepAlive(queue);
            GC.KeepAlive(sink);
        });

        foreach (var initialized in new[] { false, true })
            Test("PERF-10 实际VM：" + (initialized ? "增量" : "首读") + "重入新sink不把旧分类尾加入新visit", () =>
            {
                using var fixture = new LogFixture(10, initialized);
                var changed = false;
                fixture.Vm.Rows.CollectionChanged += (_, args) =>
                {
                    if (args.Action != NotifyCollectionChangedAction.Add || changed) return;
                    changed = true;
                    var next = new RingBufferLogSink(10);
                    next.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Info, "新分类", "新日志", null));
                    fixture.Attach(next);
                    fixture.RefreshLog();
                };
                fixture.Sink.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Info, "旧分类A", "旧一", null));
                fixture.Sink.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Info, "旧分类B", "旧二", null));
                fixture.Queue.DrainOne();
                fixture.Matches();
                Assert.True(new[] { "全部", "新分类" }.SequenceEqual(fixture.Vm.Categories));
                Assert.Equal(1L, fixture.Watermark);
                Assert.Equal(0, fixture.Queue.Count);
            });

        foreach (var changeAt in new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add })
            Test("PERF-10 实际VM：" + changeAt + "通知重入筛选不重复当前项／漏分类淘汰", () =>
            {
                using var fixture = new LogFixture(2);
                fixture.Sink.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Error, "已淘汰分类", "旧错", null));
                fixture.Sink.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Info, "保留分类", "旧信息", null));
                fixture.Queue.DrainOne();
                var changed = false;
                fixture.Vm.Rows.CollectionChanged += (_, args) =>
                {
                    if (args.Action != changeAt || changed) return;
                    changed = true;
                    fixture.Vm.SelectedLevel = 3;
                };
                fixture.Sink.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Error, "新分类", "新错", null));
                fixture.Queue.DrainOne();
                Assert.True(changed);
                Assert.Equal(1, fixture.Vm.Rows.Count);
                Assert.Equal("新错", fixture.Vm.Rows.Single().Message);
                Assert.True(new[] { "全部", "保留分类", "新分类" }.SequenceEqual(fixture.Vm.Categories));
                Assert.Equal(3L, fixture.Watermark);
                Assert.Equal("1 / 2 条内存日志", fixture.Vm.LogCount);
            });

        Test("PERF-10 实际VM：分类Reset期间重入新sink不继续旧分类构建", () =>
        {
            using var fixture = new LogFixture(10, initialized: false);
            var changed = false;
            fixture.Vm.Categories.CollectionChanged += (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Reset || changed) return;
                changed = true;
                var next = new RingBufferLogSink(10);
                next.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Info, "新分类", "新日志", null));
                fixture.Attach(next);
                fixture.RefreshLog();
            };
            fixture.Sink.Write(new LogEntry(DateTimeOffset.UnixEpoch, LogLevel.Info, "旧分类", "旧日志", null));
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.True(new[] { "全部", "新分类" }.SequenceEqual(fixture.Vm.Categories));
            Assert.Equal(1L, fixture.Watermark);
        });

        Test("PERF-10 实际VM：淘汰Remove重入同水位Refresh不重复末项", () =>
        {
            using var fixture = new LogFixture(1);
            fixture.Sink.Write(Entry(1));
            fixture.Queue.DrainOne();
            var refreshed = false;
            fixture.Vm.Rows.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Remove && !refreshed)
                {
                    refreshed = true;
                    fixture.RefreshLog();
                }
            };
            fixture.Sink.Write(Entry(2));
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(2L, fixture.Watermark);
            Assert.Equal(1, fixture.Vm.Rows.Count);
        });

        Test("PERF-10 实际VM：回调执行异常也释放pending，下条可唤醒", () =>
        {
            using var fixture = new LogFixture(10);
            NotifyCollectionChangedEventHandler fail = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add) throw new InvalidOperationException("fixture UI failure");
            };
            fixture.Vm.Rows.CollectionChanged += fail;
            fixture.Sink.Write(Entry(1));
            Assert.Throws<InvalidOperationException>(() => fixture.Queue.DrainOne());
            fixture.Vm.Rows.CollectionChanged -= fail;
            fixture.Sink.Write(Entry(2));
            Assert.Equal(1, fixture.Queue.Count);
            fixture.Queue.DrainOne();
            fixture.Matches();
            Assert.Equal(2L, fixture.Watermark);
        });

        Test("PERF-10 实际VM：并发写与UI执行交错不lost wakeup，不漏不重", () =>
        {
            using var fixture = new LogFixture(300);
            Exception? failed = null;
            var writer = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 4_000; i++)
                    {
                        fixture.Sink.Write(Entry(i));
                        if (i % 13 == 0) Thread.Yield();
                    }
                }
                catch (Exception error) { failed = error; }
            }) { IsBackground = true };
            writer.Start();
            while (writer.IsAlive)
            {
                if (!fixture.Queue.TryDrainOne()) Thread.Yield();
                Assert.True(fixture.Queue.Count <= 1);
            }
            Assert.True(writer.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(failed);
            fixture.Queue.Drain();
            fixture.Matches();
            Assert.Equal(4_000L, fixture.Watermark);
        });
    }

    private static LogEntry Entry(int number) => new(DateTimeOffset.UnixEpoch.AddTicks(number), LogLevel.Info, "fixture", $"日志{number}", null);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Abandon(RingBufferLogSink sink, LogQueue queue)
    {
        using var fixture = new LogFixture(sink, queue);
        for (var i = 0; i < 10_000; i++) sink.Write(Entry(i));
        fixture.Vm.Detach();
        return new WeakReference(fixture.Vm);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    private sealed class LogFixture : IDisposable
    {
        private readonly AppSettings _settings = new();
        private readonly PlaybackService _playback;
        internal LogFixture(int capacity, bool initialized = true) : this(new RingBufferLogSink(capacity), null, initialized) { }
        internal LogFixture(RingBufferLogSink sink, LogQueue? queue = null, bool initialized = true)
        {
            Queue = queue ?? new LogQueue();
            _playback = new PlaybackService(null!, _settings,
                () => throw new AssertionException("PERF 测试禁止启动播放"), null!, allowPlayback: false);
            Attach(sink);
            if (initialized) RefreshLog();
        }
        internal RingBufferLogSink Sink { get; private set; } = null!;
        internal DiagnosticsViewModel Vm { get; } = new();
        internal LogQueue Queue { get; }
        internal long Watermark => (long)typeof(DiagnosticsViewModel)
            .GetField("_logWatermark", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Vm)!;
        internal void Attach(RingBufferLogSink sink)
        {
            Sink = sink;
            Vm.Attach(null!, new SettingsService(null!, _settings), null!, null!, _playback,
                new AppPaths(Path.Combine(AppContext.BaseDirectory, "unused-diagnostics")), Queue, null!, null!, sink);
        }
        internal void RefreshLog() => typeof(DiagnosticsViewModel)
            .GetMethod("ReadLog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Vm, null);
        internal void Matches()
        {
            var snapshot = Sink.Snapshot();
            Assert.True(snapshot.Reverse().SequenceEqual(Vm.Rows.Select(row => row.Entry)), "实际VM必须准确显示缓冲保留尾部");
            Assert.Equal(snapshot.Count, Vm.Rows.Count);
            Assert.Equal(snapshot.Count == 0, Vm.ShowEmptyLogs);
        }
        public void Dispose() => Vm.Dispose();
    }

    private sealed class LogQueue : IUiDispatcher
    {
        private readonly object _gate = new();
        private readonly Queue<Action> _pending = new();
        private int _posts;
        internal int Posts => Volatile.Read(ref _posts);
        internal int Count { get { lock (_gate) return _pending.Count; } }
        internal bool RejectNext { get; set; }
        internal bool ThrowNext { get; set; }
        internal Action? DuringPost { get; set; }
        public bool Post(Action work)
        {
            Interlocked.Increment(ref _posts);
            var hook = DuringPost;
            DuringPost = null;
            hook?.Invoke();
            if (ThrowNext) { ThrowNext = false; throw new InvalidOperationException("fixture rejection"); }
            if (RejectNext) { RejectNext = false; return false; }
            lock (_gate) _pending.Enqueue(work);
            return true;
        }
        public void Run(Action work) => throw new AssertionException("日志必须用Post，不能内联Run");
        internal bool TryDrainOne()
        {
            Action work;
            lock (_gate)
            {
                if (_pending.Count == 0) return false;
                work = _pending.Dequeue();
            }
            work();
            return true;
        }
        internal void DrainOne() => Assert.True(TryDrainOne(), "必须有真实VM排队回调");
        internal void Drain()
        {
            var budget = 10_000;
            while (TryDrainOne()) Assert.True(--budget > 0, "意外无限回调队列");
        }
    }
}
