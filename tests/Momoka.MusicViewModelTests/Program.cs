using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.MusicViewModelTests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var cases = new (string Name, Func<Task> Run)[]
        {
            ("停止拒绝迟到 metadata，原生后端从未启动", StopMetadataAsync),
            ("停止拒绝迟到队列载入，并清空播放栏与队列", StopLoaderAsync),
            ("队列未载完时切槽不能丢弃余页", QueueLoadingNavigationAsync),
            ("未提交搜索不能污染显示列表的分页与播放队列", DraftSearchAsync),
            ("旧视频事件排队不撤销新的音乐意图", QueuedVideoAsync),
            ("自动下一曲拒绝上一首迟到收藏", LateFavoriteAsync),
            ("确认音量保存并用于下一曲启动", VolumeNextAsync),
            ("队列载入收齐 80 加 20 页，离页后仍归音乐拥有", CompleteQueueAsync),
            ("队列第二页 HTTP 挂起时停止拒绝迟到余页", StopSecondPageAsync),
            ("旧音频事件排队不覆盖新歌曲准备", QueuedAudioAsync),
            ("上一首拖动结束不能 seek 新歌曲", OldScrubAsync),
            ("页面释放后迟到列表不回填，重新附加能正常读取", ReleaseListAsync)
        };
        var failures = 0;
        foreach (var (name, run) in cases)
        {
            try { await run().WaitAsync(TimeSpan.FromSeconds(10)); Console.WriteLine("通过 " + name); }
            catch (Exception error) { failures++; Console.WriteLine("失败 " + name + "\n" + error); }
        }
        Console.WriteLine($"共 {cases.Length} 项，通过 {cases.Length - failures}，失败 {failures}，跳过 0");
        return failures == 0 ? 0 : 1;
    }

    private static async Task StopMetadataAsync()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        CancellationToken received = default;
        fixture.Transport.Reply = async (request, token) =>
        {
            if (request.Path.EndsWith("/Items/a", StringComparison.Ordinal))
            { received = token; entered.TrySetResult(); await release.Task; }
            return fixture.Transport.Default(request);
        };
        var play = fixture.Music.PlayAsync(fixture.Track("a"));
        await entered.Task;
        await fixture.Music.StopAsync();
        Require(received.IsCancellationRequested, "metadata 的请求未取消");
        Require(!fixture.Music.Visible && fixture.Music.Queue.Count == 0, "停止未立即清空栏与队列");
        release.TrySetResult();
        await play;
        Require(fixture.Backend.Starts.Count == 0, "迟到 metadata 启动了后端");
    }

    private static async Task QueueLoadingNavigationAsync()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        var a = fixture.Track("a");
        var b = fixture.Track("b");
        var c = fixture.Track("c");
        var play = fixture.Music.PlayAsync(a, [a, b], async _ => { entered.TrySetResult(); await release.Task; return new[] { a, b, c }; });
        await entered.Task;
        await fixture.Music.NextAsync();
        await fixture.Music.SelectAsync(1);
        Require(fixture.Backend.Starts.Count == 0, "余页未齐时从部分队列起播");
        release.TrySetResult();
        await UntilAsync(() => fixture.Backend.Handles.Length == 1);
        Require(fixture.Music.Queue.Count == 3, "切槽丢失余页");
        await fixture.Music.StopAsync();
        await play;
    }

    private static async Task DraftSearchAsync()
    {
        await using var fixture = new Fixture();
        fixture.Transport.Reply = (request, _) => Task.FromResult(Page(fixture.Transport, request.Query));
        using var library = fixture.Library();
        await library.ReloadAsync();
        library.Search = "尚未提交";
        await library.LoadMoreAsync();
        _ = await library.CaptureQueueLoader()(default);
        Require(fixture.Transport.Requests.All(request => !request.Query.Contains("SearchTerm", StringComparison.Ordinal)),
            "旧列表采用了未提交的搜索词");
    }

    private static async Task StopLoaderAsync()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        CancellationToken received = default;
        var play = fixture.Music.PlayAsync(fixture.Track("a"), loadQueue: async token =>
        { received = token; entered.TrySetResult(); await release.Task; return [fixture.Track("a"), fixture.Track("b")]; });
        await entered.Task;
        await fixture.Music.StopAsync();
        Require(received.IsCancellationRequested, "queue loader 未取消");
        release.TrySetResult();
        await play;
        Require(!fixture.Music.Visible && fixture.Music.Queue.Count == 0 && fixture.Backend.Starts.Count == 0,
            "迟到队列在停止后回填或起播");
    }

    private static async Task QueuedVideoAsync()
    {
        await using var fixture = new Fixture();
        var video = fixture.Track("video");
        video.Type = EmbyItemType.Movie;
        var videoPlay = fixture.Service.PlayAsync(new PlaybackTicket { Item = video, Source = video.DefaultMediaSource! }, fixture.Scope, default);
        await UntilAsync(() => fixture.Backend.Handles.Length == 1);
        await fixture.Backend.Handles[0].Listening.Task;
        Require(fixture.Ui.Pending > 0, "没有捕获视频事件排队");
        var entered = Signal();
        var release = Signal();
        fixture.Transport.Reply = async (request, _) =>
        {
            if (request.Path.EndsWith("/Items/a", StringComparison.Ordinal)) { entered.TrySetResult(); await release.Task; }
            return fixture.Transport.Default(request);
        };
        fixture.Music.BeforeStart = () => fixture.Service.StopAsync();
        var play = fixture.Music.PlayAsync(fixture.Track("a"));
        await entered.Task;
        fixture.Ui.Drain();
        Require(fixture.Music.HasIntent && fixture.Music.Title == "a", "旧视频回调取消或污染音乐准备");
        release.TrySetResult();
        await UntilAsync(() => fixture.Backend.Handles.Length == 2);
        await fixture.Backend.Handles[1].Listening.Task;
        fixture.Ui.Drain();
        Require(fixture.Service.IsAudioOnly && fixture.Music.Title == "a", "音乐未在旧视频退出后启动");
        await fixture.Music.StopAsync();
        await Task.WhenAll(play, videoPlay);
    }

    private static async Task LateFavoriteAsync()
    {
        await using var fixture = new Fixture();
        var a = fixture.Track("a");
        var b = fixture.Track("b");
        var entered = Signal();
        var release = Signal();
        fixture.Transport.Reply = async (request, _) =>
        {
            if (request.Path.EndsWith("/FavoriteItems/a", StringComparison.Ordinal))
            { entered.TrySetResult(); await release.Task; return Transport.Json(new EmbyUserData { IsFavorite = true }); }
            return fixture.Transport.Default(request);
        };
        var play = fixture.Music.PlayAsync(a, [a, b]);
        await UntilAsync(() => fixture.Backend.Handles.Length == 1);
        await fixture.Backend.Handles[0].Listening.Task;
        fixture.Ui.Drain();
        var favorite = fixture.Music.ToggleFavoriteAsync();
        await entered.Task;
        fixture.Backend.Handles[0].End();
        await UntilAsync(() => fixture.Backend.Handles.Length == 2);
        await fixture.Backend.Handles[1].Listening.Task;
        fixture.Ui.Drain();
        release.TrySetResult();
        await favorite;
        Require(fixture.Music.Title == "b" && !fixture.Music.Favorite, "A 收藏回调污染 B");
        await fixture.Music.StopAsync();
        await play;
    }

    private static async Task VolumeNextAsync()
    {
        await using var fixture = new Fixture();
        var a = fixture.Track("a");
        var play = fixture.Music.PlayAsync(a, [a, fixture.Track("b")]);
        await UntilAsync(() => fixture.Backend.Handles.Length == 1);
        await fixture.Backend.Handles[0].Listening.Task;
        fixture.Ui.Drain();
        await fixture.Music.SetVolumeAsync(30);
        Require(fixture.Settings.Settings.Audio.Volume == 30 && fixture.Settings.Saves > 0, "确认音量未进入保存路径");
        fixture.Backend.Handles[0].End();
        await UntilAsync(() => fixture.Backend.Handles.Length == 2);
        await fixture.Backend.Handles[1].Listening.Task;
        Require(fixture.Backend.Handles[1].Status.Volume == 30, "下一曲仍以旧音量起播");
        await fixture.Music.StopAsync();
        await play;
    }

    private static async Task CompleteQueueAsync()
    {
        await using var fixture = new Fixture();
        fixture.Transport.Reply = (request, _) => Task.FromResult(request.Path.EndsWith("/Items", StringComparison.Ordinal)
            ? Page(fixture.Transport, request.Query) : fixture.Transport.Default(request));
        using var library = fixture.Library();
        await library.ReloadAsync();
        Require(library.Rows.Count == 80 && library.MoreAvailable, "首屏不是 80 首带后页");
        var selected = library.Rows[0].Item;
        var loader = library.CaptureQueueLoader();
        library.Release();
        var play = fixture.Music.PlayAsync(selected, library.Tracks, loader);
        await UntilAsync(() => fixture.Backend.Handles.Length == 1);
        await fixture.Backend.Handles[0].Listening.Task;
        Require(fixture.Music.Queue.Count == 100, "音乐队列缺少第 81-100 首");
        Require(fixture.Music.Queue[99].Id == "t99", "末页次序错误");
        Require(fixture.Transport.Requests.Any(request => request.Query.Contains("StartIndex=80", StringComparison.Ordinal)), "没有请求实际第二页");
        await fixture.Music.StopAsync();
        await play;
    }

    private static async Task ReleaseListAsync()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        fixture.Transport.Reply = async (request, _) =>
        { entered.TrySetResult(); await release.Task; return Page(fixture.Transport, request.Query); };
        using var library = fixture.Library();
        var loading = library.ReloadAsync();
        await entered.Task;
        library.Release();
        release.TrySetResult();
        await loading;
        Require(library.Rows.Count == 0, "离页后旧列表回填");
        fixture.Transport.Reply = (request, _) => Task.FromResult(Page(fixture.Transport, request.Query));
        using var next = fixture.Library();
        await next.ReloadAsync();
        Require(next.Rows.Count == 80 && next.IsReady, "新页面未正常恢复");
    }

    private static async Task StopSecondPageAsync()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        CancellationToken received = default;
        fixture.Transport.Reply = async (request, token) =>
        {
            if (request.Query.Contains("StartIndex=80", StringComparison.Ordinal))
            { received = token; entered.TrySetResult(); await release.Task; }
            return request.Path.EndsWith("/Items", StringComparison.Ordinal)
                ? Page(fixture.Transport, request.Query) : fixture.Transport.Default(request);
        };
        using var library = fixture.Library();
        await library.ReloadAsync();
        var play = fixture.Music.PlayAsync(library.Rows[0].Item, library.Tracks, library.CaptureQueueLoader());
        await entered.Task;
        await fixture.Music.StopAsync();
        Require(received.IsCancellationRequested, "真实余页 HTTP 没收到取消");
        release.TrySetResult();
        await play;
        Require(fixture.Music.Queue.Count == 0 && fixture.Backend.Starts.Count == 0 && !fixture.Music.Visible,
            "停止后余页填回音乐队列或开始播放");
    }

    private static async Task QueuedAudioAsync()
    {
        await using var fixture = new Fixture();
        var a = fixture.Track("a");
        var b = fixture.Track("b");
        var first = fixture.Music.PlayAsync(a);
        await UntilAsync(() => fixture.Backend.Handles.Length == 1);
        await fixture.Backend.Handles[0].Listening.Task;
        fixture.Backend.Handles[0].Publish(90);
        var entered = Signal();
        var release = Signal();
        fixture.Transport.Reply = async (request, _) =>
        {
            if (request.Path.EndsWith("/Items/b", StringComparison.Ordinal)) { entered.TrySetResult(); await release.Task; }
            return fixture.Transport.Default(request);
        };
        var second = fixture.Music.PlayAsync(b);
        await entered.Task;
        fixture.Ui.Drain();
        Require(fixture.Music.Title == "b" && fixture.Music.Position == 0 && fixture.Music.Preparing,
            "旧音频标题/position回调覆盖 B 准备");
        release.TrySetResult();
        await UntilAsync(() => fixture.Backend.Handles.Length == 2);
        await fixture.Backend.Handles[1].Listening.Task;
        await fixture.Music.StopAsync();
        await Task.WhenAll(first, second);
    }

    private static async Task OldScrubAsync()
    {
        await using var fixture = new Fixture();
        var a = fixture.Track("a");
        var first = fixture.Music.PlayAsync(a);
        await UntilAsync(() => fixture.Backend.Handles.Length == 1);
        await fixture.Backend.Handles[0].Listening.Task;
        fixture.Ui.Drain();
        var drag = fixture.Music.BeginScrub();
        var second = fixture.Music.PlayAsync(fixture.Track("b"));
        await UntilAsync(() => fixture.Backend.Handles.Length == 2);
        await fixture.Backend.Handles[1].Listening.Task;
        fixture.Ui.Drain();
        await fixture.Music.EndScrubAsync(drag, 75);
        Require(!fixture.Backend.Handles[1].Commands.Any(command => command[0] == "seek"),
            "A的拖动release对B发了seek");
        await fixture.Music.StopAsync();
        await Task.WhenAll(first, second);
    }

    private static System.Net.Http.HttpResponseMessage Page(Transport transport, string query)
    {
        var start = query.Contains("StartIndex=80", StringComparison.Ordinal) ? 80 : 0;
        return Transport.Json(new ItemsResult
        {
            Items = Enumerable.Range(start, start == 0 ? 80 : 20)
            .Select(index => transport.Track("t" + index)).ToList(),
            TotalRecordCount = 100
        });
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        { if (condition()) return; await Task.Delay(5); }
        throw new TimeoutException("未收到预期的真实控制流事件");
    }
}
