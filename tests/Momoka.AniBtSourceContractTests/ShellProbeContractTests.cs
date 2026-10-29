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
        Program.AsyncCase("AniBT Shell 载入自动显示目标，打开不写入，一键操作可达", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            AniBtShellProbeSource.AssertLoaded(dialog);
            AssertPrepared(dialog);
            Assert.Equal("Test.S01E01.mkv", f.MatchNames.Single());
            Assert.Equal(2, f.Requests.Count(request => request.Contains("/Items/RemoteSearch/Series", StringComparison.Ordinal)));
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 多候选仍选首项并自动显示，关闭零写入", async queue =>
        {
            using var f = new ProbeFixture(manual: true);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            AniBtShellProbeSource.AssertManual(dialog);
            var row = dialog.ViewModel.Rows.Single();
            Assert.Equal(2, row.Candidates.Count);
            Assert.Equal(123L, row.Selected!.BgmId);
            Assert.Equal("2 个候选", row.MatchNote);
            AssertPrepared(dialog);
            Assert.True(dialog.CloseForFixture());
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.False(dialog.ViewModel.CanSubmit);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 自动封存实际整剧和精确候选，不识别单集", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            AssertPrepared(dialog);
            Assert.True(f.Requests.Any(request => request.Contains("/Items/anime-series?", StringComparison.Ordinal)));
            Assert.False(dialog.HasControl("CancelPreview"));
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("RetryPrepare", "Visibility"));
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 自动读取失败结束 Busy，恢复入口不冒充可提交", async queue =>
        {
            using var f = new ProbeFixture();
            f.FailPreviewRead();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            Assert.True(dialog.ViewModel.IsReady && !dialog.ViewModel.Busy);
            Assert.False(dialog.ViewModel.CanSubmit);
            Assert.Equal("重新整理", dialog.PrimaryButtonText);
            Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, dialog.ViewModel.NoticeSeverity);
            Assert.Equal("整理目标读取失败，本次没有写入", dialog.ViewModel.NoticeTitle);
            Assert.Equal(Visibility.Visible, dialog.BoundValue("RetryPrepare", "Visibility"));
            Assert.Equal("", dialog.ViewModel.Rows.Single().PreviewDetails);
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 清除候选和关闭均撤销封存凭证", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            AssertPrepared(dialog);
            var row = dialog.ViewModel.Rows.Single();
            var original = row.Selected;
            row.Selected = null;
            Assert.False(dialog.ViewModel.CanSubmit);
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("PreparedPlan", "Visibility", row));
            var before = f.Requests.Length;
            await dialog.ViewModel.SubmitAsync();
            Assert.Equal(before, f.Requests.Length);
            row.Selected = original;
            await queue.IdleAsync();
            AssertPrepared(dialog);
            Assert.True(dialog.CloseForFixture());
            await dialog.ViewModel.SubmitAsync();
            Assert.False(dialog.ViewModel.CanPreview);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 改选或聚合切来源不能借用旧目标", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            AssertPrepared(dialog);
            var row = dialog.ViewModel.Rows.Single();
            row.Selected = null;
            Assert.False(dialog.ViewModel.CanSubmit);
            f.Source.SwitchToOther();
            Assert.True(f.Source.Scope.IsAvailable);
            Assert.False(f.Source.Scope.IsCurrent);
            var before = f.Requests.Length;
            row.Selected = row.Candidates[1];
            await queue.IdleAsync();
            await dialog.ViewModel.PreviewAsync();
            await dialog.ViewModel.SubmitAsync();
            Assert.Equal(before, f.Requests.Length);
            Assert.False(dialog.ViewModel.CanPreview);
            f.AssertReadOnly();
        });
        Program.AsyncCase("AniBT Shell 实际 modal 失效关闭自动目标对话框，零写入", async queue =>
        {
            using var f = new ProbeFixture();
            var dialog = f.Dialog();
            var modal = new ShellProbeDialogBoundary(dialog);
            var shown = AniBtShellProbeSource.ShowAsync(modal, f.Source.MoviePilot);
            await queue.IdleAsync();
            AssertPrepared(dialog);
            Assert.False(shown.IsCompleted);
            f.Source.Replies.OnRequest = request =>
            {
                if (request.Path.Contains("/dashboard/system", StringComparison.Ordinal))
                    throw new HttpRequestException("offline fixture");
            };
            Assert.False(await f.Source.MoviePilot.RefreshAvailabilityAsync());
            Assert.False(shown.IsCompleted);
            modal.DispatcherQueue.Drain();
            await queue.IdleAsync();
            Assert.Equal(ShellProbeDialogResult.None, await shown);
            Assert.False(dialog.ViewModel.CanSubmit);
            f.AssertReadOnly();
        });
    }

    private static void AssertPrepared(AniBtScrapeDialog dialog)
    {
        Assert.True(dialog.ViewModel.CanSubmit, dialog.ViewModel.NoticeMessage ?? "自动目标没有准备好");
        Assert.Equal("重新整理", dialog.PrimaryButtonText);
        Assert.True(dialog.IsPrimaryButtonEnabled);
        Assert.False(dialog.ViewModel.NoticeOpen);
        var row = dialog.ViewModel.Rows.Single();
        Assert.Contains("anime-series", row.PreviewDetails);
        Assert.Contains("整部剧", row.PreviewDetails);
        Assert.Contains("Test Anime", row.PreviewDetails);
        Assert.Equal(Visibility.Visible, dialog.BoundValue("PreparedPlan", "Visibility", row));
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
