using Momoka.Configuration;
using Momoka.Danmaku;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

// LoadDanmakuAsync 自当前 Shell 源码提取并编译；下方仅替代播放身份、UI 呈现和宿主发送边界。
internal sealed partial class PlayerViewModel(DandanPlayClient client, AppSettings settings) : IDisposable
{
    private readonly DandanPlayClient _dandanPlay = client;
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SyntheticPlayback _playback = new();
    private CancellationTokenSource? _danmakuLoad;
    private int _danmakuRequest, _danmakuTransfer;
    private readonly int _danmakuSerial = 1;
    private IReadOnlyList<DanmakuComment> _danmakuComments = [];
    private DandanPlayEpisode? _danmakuEpisode;
    private DandanPlayEpisode? _danmakuDanmuApiEpisode;
    private bool _danmakuFetched;
    private int _danmakuDandanCount;
    private int _danmakuDanmuApiCount;
    private string _danmakuLabel = "";
    private string _danmakuWarning = "";
    private string _danmakuSourceSummary = "";
    internal AppSettings Settings { get; } = settings;
    internal bool DanmakuAvailable { get; set; } = true;
    internal string DanmakuStatus { get; set; } = "";
    internal int Generation { get; set; }
    internal int Pushes { get; private set; }
    internal bool Fetched => _danmakuFetched;
    internal IReadOnlyList<DanmakuComment> Applied => _danmakuComments;
    internal Func<Task>? ClearHook { get; set; }
    private void CancelDanmakuSearch() { }
    private int CaptureInteraction() => Generation;
    private bool IsCurrentPlayback(int context) => context == Generation && _playback.IsPlaying;
    private void ResetDanmakuInput() { }
    private void RefreshDanmakuInput() { }
    private Task ClearDanmakuAsync() => ClearHook?.Invoke() ?? Task.CompletedTask;
    private Task PingDanmakuAsync() => Task.CompletedTask;
    private Task PushDanmakuAsync() { Pushes++; return Task.CompletedTask; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _danmakuLoad?.Cancel();
        _danmakuLoad?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
    private sealed class SyntheticPlayback { internal bool IsPlaying { get; } = true; }
}

internal static class DanmakuLoadOwnershipTests
{
    private static readonly DandanPlayEpisode Episode = new(7, "fixture", "first");
    internal static void Register()
    {
        Test("PERF-05 宿主真实加载方法：缓存迟到结果不应用到换片后的 UI", () =>
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40000, 300));
            fixture.WarmAsync().GetAwaiter().GetResult();
            fixture.Transport.Forbid = true;
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Credentials.BeforeReturn = async _ => { reached.TrySetResult(); await release.Task.ConfigureAwait(false); };
            using var model = new PlayerViewModel(fixture.Client, fixture.Settings);
            var old = model.LoadDanmakuAsync(Episode);
            reached.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            model.Generation++;
            model.DanmakuStatus = "successor";
            release.SetResult();
            old.GetAwaiter().GetResult();
            Assert.Equal(0, model.Applied.Count);
            Assert.False(model.Fetched);
            Assert.Equal(0, model.Pushes);
            Assert.Equal("successor", model.DanmakuStatus);
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("PERF-05 宿主真实加载方法：新请求取消旧后台读，旧结果/错误不覆盖新播放", () =>
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40));
            fixture.WarmAsync().GetAwaiter().GetResult();
            fixture.Transport.Forbid = true;
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            fixture.Credentials.BeforeReturn = async _ =>
            {
                if (Interlocked.Increment(ref reads) == 1) { reached.TrySetResult(); await release.Task.ConfigureAwait(false); }
            };
            using var model = new PlayerViewModel(fixture.Client, fixture.Settings);
            var old = model.LoadDanmakuAsync(Episode);
            reached.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            model.Generation++;
            var next = model.LoadDanmakuAsync(Episode with { EpisodeTitle = "successor" });
            release.SetResult();
            Task.WhenAll(old, next).GetAwaiter().GetResult();
            Assert.Equal(40, model.Applied.Count);
            Assert.True(model.Fetched);
            Assert.Equal(1, model.Pushes);
            Assert.DoesNotContain("超时", model.DanmakuStatus);
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("PERF-05 宿主真实加载方法：功能收起时迟到缓存不应用，不借用下一次可用性", () =>
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40));
            fixture.WarmAsync().GetAwaiter().GetResult();
            fixture.Transport.Forbid = true;
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Credentials.BeforeReturn = async _ => { reached.TrySetResult(); await release.Task.ConfigureAwait(false); };
            using var model = new PlayerViewModel(fixture.Client, fixture.Settings);
            var old = model.LoadDanmakuAsync(Episode);
            reached.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            model.DanmakuAvailable = false;
            release.SetResult();
            old.GetAwaiter().GetResult();
            Assert.Equal(0, model.Applied.Count);
            Assert.False(model.Fetched);
            Assert.Equal(0, model.Pushes);
            fixture.Credentials.BeforeReturn = null;
            model.DanmakuAvailable = true;
            model.LoadDanmakuAsync(Episode).GetAwaiter().GetResult();
            Assert.Equal(40, model.Applied.Count);
            Assert.Equal(1, model.Pushes);
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("PERF-05 宿主真实加载方法：清空边界迟到后不开始旧缓存读", () =>
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40));
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var model = new PlayerViewModel(fixture.Client, fixture.Settings) { ClearHook = () => release.Task };
            var old = model.LoadDanmakuAsync(Episode);
            model.Generation++;
            release.SetResult();
            old.GetAwaiter().GetResult();
            Assert.Equal(0, fixture.Credentials.Calls);
            Assert.Equal(0, fixture.Transport.Requests);
            Assert.Equal(0, model.Pushes);
        });
    }
}
