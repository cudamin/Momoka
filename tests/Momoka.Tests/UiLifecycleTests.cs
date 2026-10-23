using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class UiLifecycleTests
{
    internal static void Register()
    {
        Test("播放列表：分页途中取消不能回退首屏起播", () => CheckCancelledPageAsync(throws: true).GetAwaiter().GetResult());
        Test("播放列表：不响应取消的旧分页也不能起播", () => CheckCancelledPageAsync(throws: false).GetAwaiter().GetResult());
        Test("播放列表：新查询使旧分页结果作废", () => CheckCancelledPageAsync(throws: false, replace: true).GetAwaiter().GetResult());
        Test("播放列表：正常补齐分页并排除不可播放条目", () =>
        {
            var asked = new List<int>();
            var items = PlayableList.CollectAsync(
                [Movie("1"), new EmbyItem { Id = "folder", Type = EmbyItemType.Folder }], true,
                (start, _) =>
                {
                    asked.Add(start);
                    return Task.FromResult<ItemsResult?>(new ItemsResult { Items = [Movie(start.ToString())], TotalRecordCount = 4 });
                }, () => true, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("2,3", string.Join(',', asked));
            Assert.Equal("1,2,3", string.Join(',', items.Select(item => item.Id)));
        });
        Test("播放列表：普通网络失败仍可回退已读条目", () =>
        {
            var items = PlayableList.CollectAsync([Movie("1")], true,
                (_, _) => Task.FromException<ItemsResult?>(new IOException("fixture")),
                () => true, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, items.Count);
        });
        Test("播放列表：过期任务的失败不得回退首屏", () =>
        {
            var current = true;
            var items = PlayableList.CollectAsync([Movie("1")], true,
                (_, _) =>
                {
                    current = false;
                    return Task.FromException<ItemsResult?>(new IOException("fixture"));
                }, () => current, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(0, items.Count);
        });
        Test("播放列表：无后续页不发请求，取消仍返回空", () =>
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var items = PlayableList.CollectAsync([Movie("1")], false,
                (_, _) => throw new AssertionException("不应请求下一页"),
                () => true, canceled.Token).GetAwaiter().GetResult();
            Assert.Equal(0, items.Count);
        });

        Test("播放列表：续播显示含合成下一集，显式服务器偏移不漏尾项", () =>
        {
            var offsets = new List<int>();
            var items = PlayableList.CollectAsync([Movie("synthetic"), Movie("read")], true,
                (offset, _) =>
                {
                    offsets.Add(offset);
                    return Task.FromResult<ItemsResult?>(new ItemsResult { Items = [Movie("tail")], TotalRecordCount = 2 });
                }, () => true, CancellationToken.None, startIndex: 1).GetAwaiter().GetResult();
            Assert.Equal("1", string.Join(',', offsets));
            Assert.Equal("synthetic,read,tail", string.Join(',', items.Select(item => item.Id)));
        });
        Test("播放列表：显式偏移按返回原记录数推进，不按可播条数推进", () =>
        {
            var offsets = new List<int>();
            var items = PlayableList.CollectAsync([Movie("synthetic")], true,
                (offset, _) =>
                {
                    offsets.Add(offset);
                    return Task.FromResult<ItemsResult?>(new ItemsResult
                    {
                        Items = offset == 0 ? [new EmbyItem { Id = "folder", Type = EmbyItemType.Folder }] : [Movie("tail")],
                        TotalRecordCount = 2
                    });
                }, () => true, CancellationToken.None, startIndex: 0).GetAwaiter().GetResult();
            Assert.Equal("0,1", string.Join(',', offsets));
            Assert.Equal("synthetic,tail", string.Join(',', items.Select(item => item.Id)));
        });

        Test("首页刷新：首载被轻量刷新替代仍必须请求轮播", () =>
        {
            var refresh = new HomeRefresh();
            var first = refresh.Begin();
            Assert.True(refresh.NeedsSlides(true));
            var replacement = refresh.Begin();
            Assert.True(refresh.NeedsSlides(false));
            refresh.Complete(first, includedSlides: true);
            Assert.True(refresh.NeedsSlides(false), "旧首载不得使新请求漏读轮播");
            refresh.Complete(replacement, includedSlides: true);
            Assert.False(refresh.NeedsSlides(false));
        });
        Test("首页刷新：已载轮播可以保留，完整刷新仍重新请求", () =>
        {
            var refresh = new HomeRefresh();
            refresh.Complete(refresh.Begin(), includedSlides: true);
            Assert.False(refresh.NeedsSlides(false));
            Assert.True(refresh.NeedsSlides(true));
            refresh.Complete(refresh.Begin(), includedSlides: false);
            Assert.False(refresh.NeedsSlides(false));
        });
        Test("首页刷新：重建版面作废轮播状态和旧完成通知", () =>
        {
            var refresh = new HomeRefresh();
            var prior = refresh.Begin();
            refresh.Complete(prior, includedSlides: true);
            refresh.Invalidate();
            refresh.Complete(prior, includedSlides: true);
            Assert.True(refresh.NeedsSlides(false));
        });

        Test("主页定时检查：指纹按顺序记，认不出的条目跳过", () =>
        {
            Assert.Equal("2:1\u001f2", LibraryWatch.Stamp([Movie("1"), Movie("2")]));
            Assert.Equal("2:2\u001f1", LibraryWatch.Stamp([Movie("2"), Movie("1")]));
            Assert.Equal("1:1", LibraryWatch.Stamp([Movie("1"), new EmbyItem { Id = "", Type = EmbyItemType.Movie }]));
            Assert.Equal("0:", LibraryWatch.Stamp([]));
        });
        Test("主页定时检查：没变化不报变化，新入库才报", () =>
        {
            var watch = new LibraryWatch();
            var first = Stamps(("a", [Movie("1"), Movie("2")]));

            // 从没记过的时候先报一次 —— 调用方据此在整页重读时建立基线（见 HomeViewModel.LoadAsync）。
            Assert.Equal("a", string.Join(',', watch.Changed(first)));

            watch.Reset(first);
            Assert.Equal(0, watch.Changed(first).Count);

            // 同一部剧来了新一集：服务器那边 GroupItems 并剧之后，头上那一条换人。
            var newer = Stamps(("a", [Movie("3"), Movie("1")]));
            Assert.Equal("a", string.Join(',', watch.Changed(newer)));

            watch.Reset(newer);
            Assert.Equal(0, watch.Changed(newer).Count);

            watch.Clear();
            Assert.Equal("a", string.Join(',', watch.Changed(newer)), "换账号之后这一份不再作数");
        });
        Test("主页定时检查：读不出来的来源不参与比较", () =>
        {
            var watch = new LibraryWatch();
            var both = Stamps(("a", [Movie("1")]), ("b", [Movie("9")]));
            watch.Reset(both);

            // 这一趟只读到 a（b 超时，调用方把它丢掉了）：a 没变，因此没有变化 —— 拿少了的那一份当「变了」
            // 就是多账号聚合时每隔半分钟白刷一次。
            Assert.Equal(0, watch.Changed(Stamps(("a", [Movie("1")]))).Count);

            // b 回来之后照旧和它上一次的读数比。
            Assert.Equal(0, watch.Changed(both).Count);
            Assert.Equal("b", string.Join(',', watch.Changed(Stamps(("a", [Movie("1")]), ("b", [Movie("9"), Movie("10")])))));
        });

        Test("窗口切换：快速反向共享暂停归属并只恢复一次", () =>
        {
            var pause = new HandoffPause();
            Assert.True(pause.Acquire(paused: false));
            Assert.False(pause.Release(current: true, pending: true));
            Assert.False(pause.Acquire(paused: true));
            Assert.False(pause.Release(current: true, pending: true));
            Assert.False(pause.Acquire(paused: true));
            Assert.True(pause.Release(current: true, pending: false));
            Assert.False(pause.Release(current: true, pending: false));
        });
        Test("窗口切换：用户原先暂停不能被自动恢复", () =>
        {
            var pause = new HandoffPause();
            Assert.False(pause.Acquire(paused: true));
            Assert.False(pause.Release(current: true, pending: false));
        });
        Test("窗口切换：停止或换片作废暂停恢复责任", () =>
        {
            var pause = new HandoffPause();
            Assert.True(pause.Acquire(paused: false));
            Assert.False(pause.Release(current: false, pending: false));
            Assert.False(pause.Release(current: true, pending: false));
        });
    }

    private static async Task CheckCancelledPageAsync(bool throws, bool replace = false)
    {
        using var loads = new LoadGeneration();
        var token = loads.Begin();
        var page = new TaskCompletionSource<ItemsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = PlayableList.CollectAsync([Movie("first")], true,
            (_, _) =>
            {
                entered.TrySetResult();
                return page.Task;
            }, () => loads.IsCurrent(token), token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (replace) loads.Begin();
        else loads.Cancel();
        if (throws) page.TrySetException(new OperationCanceledException(token));
        else page.TrySetResult(new ItemsResult { Items = [Movie("late")], TotalRecordCount = 2 });
        var items = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, items.Count);
    }

    private static EmbyItem Movie(string id) => new() { Id = id, Name = id, Type = EmbyItemType.Movie };

    /// <summary>一个来源此刻的指纹表，见 <see cref="LibraryWatch"/>。</summary>
    private static Dictionary<string, string> Stamps(params (string Source, EmbyItem[] Items)[] sources) =>
        sources.ToDictionary(
            entry => entry.Source,
            entry => LibraryWatch.Stamp(entry.Items),
            StringComparer.Ordinal);
}
