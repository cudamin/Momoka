using Microsoft.UI.Xaml;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Tests;

// -- shell-probe runs only these delivery regressions. Fixtures/assertions are extracted verbatim
// from ShellNavigationProbe.AniBt.cs; the actual Core, VM, dialog and modal owner remain under test.
internal static class ShellProbeContractTests
{
    internal static void Register()
    {
        Program.AsyncCase("AniBT Shell 载入后只可预览，提前 Submit 零请求", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            Console.WriteLine($"  Shell load: IsReady={dialog.ViewModel.IsReady}, Rows={dialog.ViewModel.Rows.Count}, " +
                $"CanPreview={dialog.ViewModel.CanPreview}, CanSubmit={dialog.ViewModel.CanSubmit}, " +
                $"SourceCurrent={f.Source.Scope.IsCurrent}, Selected={dialog.ViewModel.Rows.Single().Selected?.BgmId}, Button={dialog.PrimaryButtonText}");
            AniBtShellProbeSource.AssertLoaded(dialog);
            Assert.True(dialog.ViewModel.IsReady && !dialog.ViewModel.Busy && dialog.ViewModel.CanPreview);
            Assert.False(dialog.ViewModel.CanSubmit);
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.True(dialog.IsPrimaryButtonEnabled);
            Assert.Equal("Test.S01E01.mkv", f.MatchNames.Single());
            var before = f.Requests.Length;
            await dialog.ViewModel.SubmitAsync();
            Assert.Equal(before, f.Requests.Length);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 多候选仍选首项且关闭零写入", async queue =>
        {
            using var f = new ProbeFixture(manual: true);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            AniBtShellProbeSource.AssertManual(dialog);
            var row = dialog.ViewModel.Rows.Single();
            Assert.Equal(2, row.Candidates.Count);
            Assert.Equal(123L, row.Selected!.BgmId);
            Assert.Contains("多个候选", row.MatchNote);
            Assert.False(dialog.ViewModel.CanSubmit);
            Assert.True(dialog.CloseForFixture());
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.False(dialog.ViewModel.CanPreview);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 第一击封存实际整剧和精确候选，不识别单集", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            var busy = new List<bool>();
            dialog.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PageViewModel.Busy)) busy.Add(dialog.ViewModel.Busy);
            };
            await dialog.PrimaryForFixtureAsync(queue);
            AssertPrepared(dialog);
            Assert.True(busy.SequenceEqual([true, false]), "实际 primary 必须开始并结束本次 Busy，不能沿用载入阶段 IsReady");
            Assert.Equal(2, f.Requests.Count(request => request.Contains("/Items/RemoteSearch/Series", StringComparison.Ordinal)));
            Assert.True(f.Requests.Any(request => request.Contains("/Items/anime-series?", StringComparison.Ordinal)));
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 预览读取失败仍结束本次 Busy，错误不能等成可提交", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            f.FailPreviewRead();
            var busy = new List<bool>();
            dialog.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PageViewModel.Busy)) busy.Add(dialog.ViewModel.Busy);
            };
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.True(busy.SequenceEqual([true, false]));
            Assert.True(dialog.ViewModel.IsReady && !dialog.ViewModel.Busy);
            Assert.False(dialog.ViewModel.CanSubmit);
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, dialog.ViewModel.NoticeSeverity);
            Assert.Equal("预览失败，本次没有写入", dialog.ViewModel.NoticeTitle);
            Assert.True(dialog.ViewModel.NoticeOpen);
            Assert.Equal(1, f.Requests.Count(request => request.Contains("/Items/RemoteSearch/Series", StringComparison.Ordinal)));
            Assert.Equal("", dialog.ViewModel.Rows.Single().PreviewDetails);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 取消预览和关闭均撤销封存凭证", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            AssertPrepared(dialog);
            dialog.ClickForFixture("CancelPreview");
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("PreparedPlan", "Visibility", dialog.ViewModel.Rows.Single()));
            Assert.False(dialog.ViewModel.CanSubmit);
            var before = f.Requests.Length;
            await dialog.ViewModel.SubmitAsync();
            Assert.Equal(before, f.Requests.Length);
            await dialog.PrimaryForFixtureAsync(queue);
            AssertPrepared(dialog);
            Assert.True(dialog.CloseForFixture());
            await dialog.ViewModel.SubmitAsync();
            Assert.False(dialog.ViewModel.CanPreview);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 改选或聚合切来源不能借用旧确认", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            AssertPrepared(dialog);
            var row = dialog.ViewModel.Rows.Single();
            row.Selected = row.Candidates[1];
            Assert.False(dialog.ViewModel.CanSubmit);
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            f.Source.SwitchToOther();
            Assert.True(f.Source.Scope.IsAvailable);
            Assert.False(f.Source.Scope.IsCurrent);
            var before = f.Requests.Length;
            await dialog.ViewModel.PreviewAsync();
            await dialog.ViewModel.SubmitAsync();
            Assert.Equal(before, f.Requests.Length);
            Assert.False(dialog.ViewModel.CanPreview);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 实际 modal 可用性撤销关闭已预览对话框", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            var modal = new ShellProbeDialogBoundary(dialog);
            var shown = AniBtShellProbeSource.ShowAsync(modal, f.Source.MoviePilot);
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            AssertPrepared(dialog);
            Assert.False(shown.IsCompleted);
            f.Source.Replies.OnRequest = request =>
            {
                if (request.Path.Contains("/dashboard/system", StringComparison.Ordinal))
                    throw new HttpRequestException("offline fixture");
            };
            Assert.False(await f.Source.MoviePilot.RefreshAvailabilityAsync(), "实际只读可用性检查必须检测到假 MP 断线");
            Assert.False(shown.IsCompleted, "失效回调只投递关闭，由惰性 UI 队列执行");
            modal.DispatcherQueue.Drain();
            Assert.Equal(ShellProbeDialogResult.None, await shown);
            Assert.False(dialog.ViewModel.CanPreview);
            Assert.False(dialog.ViewModel.CanSubmit);
            await dialog.ViewModel.SubmitAsync();
            f.AssertReadOnly();
        });
    }

    private static void AssertPrepared(AniBtScrapeDialog dialog)
    {
        Assert.True(dialog.ViewModel.CanSubmit, dialog.ViewModel.NoticeMessage ?? "");
        Assert.Equal("确认执行", dialog.PrimaryButtonText);
        var row = dialog.ViewModel.Rows.Single();
        Assert.Equal(Visibility.Visible, dialog.BoundValue("PreparedPlan", "Visibility", row));
        Assert.Contains("anime-series", row.PreviewDetails);
        Assert.Contains("离线库内整剧", row.PreviewDetails);
        Assert.Contains("整部剧", row.PreviewDetails);
        Assert.Contains("Test Anime", row.PreviewDetails);
        Assert.Contains("2026", row.PreviewDetails);
        Assert.Contains("元数据", row.PreviewDetails);
        Assert.Contains("图片", row.PreviewDetails);
        Assert.Equal(row.PreviewDetails, dialog.BoundValue("PreviewDetailsText", "Text", row));
    }

    private sealed class ProbeFixture : IDisposable
    {
        private readonly System.Net.Http.HttpMessageHandler _embyReplies;
        private readonly System.Net.Http.HttpMessageHandler _aniBtReplies;
        private readonly EmbyHttp _http;
        private readonly AniBtClient _client;
        private readonly AniBtService _service;
        private readonly EmbyItem _item;
        internal SourceFixture Source { get; } = new();
        internal string[] Requests => AniBtShellProbeSource.Requests(_embyReplies);
        internal string[] MatchNames => AniBtShellProbeSource.MatchNames(_aniBtReplies);

        internal ProbeFixture(bool manual = false)
        {
            var input = AniBtShellProbeSource.Input();
            _embyReplies = input.Emby;
            _http = new EmbyHttp(_embyReplies);
            // Replace only the anonymous HTTP endpoint, preserving the actual current scope/identity.
            Source.Scope.IdentityState.Client = new EmbyClient(_http, Source.Emby.Connection);
            Assert.True(Source.Scope.IsCurrent && Source.Scope.IsAvailable);
            _item = Source.Scope.Bind(input.Item);
            _aniBtReplies = AniBtShellProbeSource.AniBt(manual);
            _client = new AniBtClient(_aniBtReplies);
            _service = new AniBtService(_client, Source.MoviePilot);
        }

        internal AniBtScrapeDialog Dialog() => new(_service, Source.Scope, _item);
        internal void FailPreviewRead() => AniBtShellProbeSource.FailPreviewRead(_embyReplies);
        internal void AssertReadOnly()
        {
            Source.AssertWrites(0, 0);
            Assert.True(Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal) ||
                request.StartsWith("POST ", StringComparison.Ordinal) && request.Contains("/Items/RemoteSearch/Series", StringComparison.Ordinal)),
                "实际探针回复只能接受读取，确认前和取消不能发 Apply、Refresh 或播放");
        }

        public void Dispose()
        {
            try { AssertReadOnly(); }
            finally { Source.Dispose(); _client.Dispose(); _http.Dispose(); }
        }
    }
}

// Missing modal and dispatcher endpoints only. The extracted MoviePilotDialog.ShowAsync owns
// availability, captured connection, subscription and return checks; no business guards are copied.
internal enum ShellProbeDialogResult { None }
internal sealed class ShellProbeDialogBoundary(AniBtScrapeDialog dialog)
{
    private readonly TaskCompletionSource<ShellProbeDialogResult> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ShellProbeDispatcherBoundary DispatcherQueue { get; } = new();
    internal Task<ShellProbeDialogResult> ShowAsync()
    {
        dialog.OpenForFixture();
        return _closed.Task;
    }
    internal void Hide()
    {
        if (dialog.CloseForFixture()) _closed.TrySetResult(ShellProbeDialogResult.None);
    }
}
internal sealed class ShellProbeDispatcherBoundary
{
    private readonly Queue<Action> _callbacks = new();
    internal bool TryEnqueue(Action callback)
    {
        _callbacks.Enqueue(callback);
        return true;
    }
    internal void Drain()
    {
        while (_callbacks.TryDequeue(out var callback)) callback();
    }
}
