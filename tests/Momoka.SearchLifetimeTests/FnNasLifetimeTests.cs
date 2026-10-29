using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Momoka.FnNas;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

internal static class FnNasLifetimeTests
{
    private static readonly FnNasScrapResult Candidate = new("匿名候选", "tmdb", "42", "Movie", "2026-01-01");
    internal static void Register(List<(string Name, Func<Task> Run)> cases)
    {
        void Test(string name, Func<Task> run) => cases.Add(("PERF-12 " + name, run));
        Test("打开/重开不自动搜索，未打开/关闭后也不能提交", async () =>
        {
            var searches = 0;
            var dialog = new FnNasMatchDialog("匿名", (_, _, _) => { searches++; return Task.FromResult<IReadOnlyList<FnNasScrapResult>>([Candidate]); });
            await dialog.SubmitFixture(); Check.Equal(0, searches);
            dialog.OpenFixture(); Check.Equal(0, searches); Check.False(dialog.IsPrimaryButtonEnabled);
            dialog.CloseFixture(); await dialog.SubmitFixture(); Check.Equal(0, searches);
            dialog.OpenFixture(); Check.Equal(0, searches); await dialog.SubmitFixture(); Check.Equal(1, searches);
            dialog.CloseFixture();
        });
        Test("三种搜索参数/trim/新查询令牌；空词不读且候选来自本次集合", async () =>
        {
            var calls = new List<(string Source, string Keyword, CancellationToken Token)>();
            var dialog = new FnNasMatchDialog("  匿名  ", (source, keyword, token) =>
            { calls.Add((source, keyword, token)); return Task.FromResult<IReadOnlyList<FnNasScrapResult>>([Candidate]); });
            dialog.OpenFixture();
            for (var i = 0; i < 3; i++)
            {
                dialog.SourceBox.SelectedIndex = i; await dialog.SubmitFixture();
                Check.Equal(i switch { 1 => FnNasItemActions.SearchByImdbId, 2 => FnNasItemActions.SearchByTmdbId, _ => FnNasItemActions.SearchByTitle }, calls[i].Source);
                Check.Equal("匿名", calls[i].Keyword); Check.True(calls[i].Token.CanBeCanceled);
                Check.False(calls[i].Token.IsCancellationRequested);
                if (i > 0) Check.True(calls[i - 1].Token.IsCancellationRequested);
            }
            var rows = (IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!;
            dialog.Results.Select(rows[0]); Check.Same(rows[0], dialog.Selected); Check.True(dialog.IsPrimaryButtonEnabled);
            dialog.Results.Select(new FnNasScrapCandidate(Candidate)); Check.Null(dialog.Selected); Check.False(dialog.IsPrimaryButtonEnabled);
            dialog.KeywordBox.Text = "  "; await dialog.SubmitFixture(); Check.Equal(3, calls.Count); Check.True(dialog.Problem.IsOpen);
            dialog.CloseFixture();
        });
        Test("忙时重复提交只读一次，关闭立即cancel等待并收结果；迟到响应不复活UI", async () =>
        {
            var hold = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>(); CancellationToken token = default; var calls = 0;
            var dialog = new FnNasMatchDialog("匿名", (_, _, value) => { calls++; token = value; return hold.Task; });
            dialog.OpenFixture(); var search = dialog.SubmitFixture(); await dialog.SubmitFixture();
            Check.Equal(1, calls); Check.Equal(Visibility.Visible, dialog.Busy.Visibility); Check.False(dialog.SearchButton.IsEnabled);
            dialog.CloseFixture(); Check.True(token.IsCancellationRequested); await search.WaitAsync(TimeSpan.FromSeconds(1));
            Check.Null(dialog.Results.ItemsSource); Check.False(dialog.SearchButton.IsEnabled); Check.Equal(Visibility.Collapsed, dialog.Busy.Visibility);
            hold.SetResult([Candidate]); await Task.Delay(5);
            Check.Null(dialog.Results.ItemsSource); Check.False(dialog.Problem.IsOpen); Check.False(dialog.SearchButton.IsEnabled);
        });
        Test("关闭后迟到error静默，新访问可再次搜索且令牌独立", async () =>
        {
            var old = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>(); var fresh = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>();
            var tokens = new List<CancellationToken>(); var calls = 0;
            var dialog = new FnNasMatchDialog("匿名", (_, _, token) => { tokens.Add(token); return ++calls == 1 ? old.Task : fresh.Task; });
            dialog.OpenFixture(); var abandoned = dialog.SubmitFixture(); dialog.CloseFixture();
            dialog.OpenFixture(); dialog.KeywordBox.Text = "新关键词"; var current = dialog.SubmitFixture();
            Check.True(tokens[0].IsCancellationRequested); Check.False(tokens[1].IsCancellationRequested);
            await abandoned; old.SetException(new HttpRequestException("匿名迟到错误")); await Task.Delay(5);
            Check.False(dialog.Problem.IsOpen); Check.Equal(Visibility.Visible, dialog.Busy.Visibility); Check.False(dialog.SearchButton.IsEnabled);
            fresh.SetResult([Candidate]); await current; Check.Equal(1, ((IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!).Count);
            Check.False(dialog.Problem.IsOpen); Check.True(dialog.SearchButton.IsEnabled); dialog.CloseFixture();
        });
        Test("network-complete旧success排队：重开新查询时不得覆盖结果或finally忙态", () => QueuedCompletion(error: false));
        Test("network-complete旧error排队：重开新查询时不得开错误条或finally忙态", () => QueuedCompletion(error: true));
        Test("旧cancel continuation尚在UI队列，新访问/新查询仍独占loading", async () =>
        {
            var queue = new QueuedContext(); var old = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>();
            var fresh = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>(); var calls = 0;
            var dialog = new FnNasMatchDialog("匿名", (_, _, _) => ++calls == 1 ? old.Task : fresh.Task);
            dialog.OpenFixture(); var abandoned = Under(queue, dialog.SubmitFixture);
            dialog.CloseFixture(); await Check.Until(() => queue.Pending.Count > 0);
            dialog.OpenFixture(); var current = dialog.SubmitFixture(); queue.Drain(); await abandoned;
            Check.False(dialog.SearchButton.IsEnabled); Check.Equal(Visibility.Visible, dialog.Busy.Visibility); Check.False(dialog.Problem.IsOpen);
            fresh.SetResult([Candidate]); await current; old.SetResult([]); queue.Drain();
            Check.Equal(1, ((IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!).Count); Check.True(dialog.SearchButton.IsEnabled);
            dialog.CloseFixture();
        });
        Test("同次访问改词显式提交替代旧读，旧finally不抢新查询所有权", async () =>
        {
            var first = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>();
            var second = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>();
            var tokens = new List<CancellationToken>(); var calls = 0;
            var dialog = new FnNasMatchDialog("第一词", (_, _, token) => { tokens.Add(token); return ++calls == 1 ? first.Task : second.Task; });
            dialog.OpenFixture(); var old = dialog.SubmitFixture();
            dialog.KeywordBox.Text = "第二词"; var fresh = dialog.SubmitFixture();
            Check.Equal(2, calls); Check.True(tokens[0].IsCancellationRequested); Check.False(tokens[1].IsCancellationRequested);
            await old; first.SetException(new HttpRequestException("被替代的旧错误"));
            Check.False(dialog.Problem.IsOpen); Check.False(dialog.SearchButton.IsEnabled); Check.Equal(Visibility.Visible, dialog.Busy.Visibility);
            await dialog.SubmitFixture(); Check.Equal(2, calls);
            second.SetResult([Candidate with { Title = "第二词结果" }]); await fresh;
            Check.Equal("第二词结果", ((IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!)[0].Title);
            Check.True(dialog.SearchButton.IsEnabled); dialog.CloseFixture();
        });
        Test("当前读取失败正常反馈并恢复重试，不静默掩盖真正错误", async () =>
        {
            var dialog = new FnNasMatchDialog("匿名", (_, _, _) => Task.FromException<IReadOnlyList<FnNasScrapResult>>(new HttpRequestException("匿名网络错误")));
            dialog.OpenFixture(); await dialog.SubmitFixture();
            Check.True(dialog.Problem.IsOpen); Check.Contains("搜索失败", dialog.Problem.Message);
            Check.True(dialog.SearchButton.IsEnabled); Check.Equal(Visibility.Collapsed, dialog.Busy.Visibility);
            Check.False(dialog.IsPrimaryButtonEnabled); dialog.CloseFixture();
        });
        Test("新查询清上次选择且禁用确认；旧行不可再次选中", async () =>
        {
            var hold = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>(); var calls = 0;
            var dialog = new FnNasMatchDialog("匿名", (_, _, _) => ++calls == 1 ? Task.FromResult<IReadOnlyList<FnNasScrapResult>>([Candidate]) : hold.Task);
            dialog.OpenFixture(); await dialog.SubmitFixture();
            var oldRow = ((IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!)[0]; dialog.Results.Select(oldRow);
            Check.True(dialog.IsPrimaryButtonEnabled);
            var second = dialog.SubmitFixture(); Check.Null(dialog.Selected); Check.False(dialog.IsPrimaryButtonEnabled); Check.Null(dialog.Results.ItemsSource);
            Check.True(dialog.MatchFixture().Cancel); dialog.Results.Select(oldRow); Check.Null(dialog.Selected);
            hold.SetResult([]); await second; Check.True(dialog.Problem.IsOpen); Check.Contains("没有找到", dialog.Problem.Message);
            dialog.CloseFixture();
        });
        Test("Primary关闭保留确认候选；重开清上批选择/错误/列表而不自动读", async () =>
        {
            var calls = 0; var dialog = new FnNasMatchDialog("匿名", (_, _, _) => { calls++; return Task.FromResult<IReadOnlyList<FnNasScrapResult>>([Candidate]); });
            dialog.OpenFixture(); await dialog.SubmitFixture();
            var selected = ((IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!)[0]; dialog.Results.Select(selected);
            Check.False(dialog.MatchFixture().Cancel); dialog.CloseFixture(); Check.Same(selected, dialog.Selected); Check.Null(dialog.Results.ItemsSource);
            dialog.OpenFixture(); Check.Null(dialog.Selected); Check.False(dialog.IsPrimaryButtonEnabled); Check.Null(dialog.Results.ItemsSource);
            Check.False(dialog.Problem.IsOpen); Check.Equal(1, calls); dialog.CloseFixture();
        });
        Test("空结果回填同步关闭重开，新访问不继承旧空结果错误条", async () =>
        {
            var dialog = new FnNasMatchDialog("匿名", (_, _, _) => Task.FromResult<IReadOnlyList<FnNasScrapResult>>([]));
            dialog.OpenFixture(); var reentered = false;
            dialog.Results.ItemsSourceChanged = value =>
            {
                if (!reentered && value is not null)
                { reentered = true; dialog.CloseFixture(); dialog.OpenFixture(); }
            };
            await dialog.SubmitFixture(); Check.True(reentered);
            Check.False(dialog.Problem.IsOpen); Check.Null(dialog.Results.ItemsSource); Check.True(dialog.SearchButton.IsEnabled);
            dialog.CloseFixture(); Check.True(dialog.MatchFixture().Cancel); Check.False(dialog.Problem.IsOpen);
        });
        Test("25个关闭的非合作读取仍pending也不保留对话框", async () =>
        {
            var holds = new List<TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>>(); var references = new List<WeakReference>();
            for (var i = 0; i < 25; i++) references.Add(await CreateClosedAsync(holds));
            MoviePilotLifetimeTests.Collect();
            Check.Equal(0, references.Count(reference => reference.IsAlive)); Check.True(holds.All(hold => !hold.Task.IsCompleted));
            foreach (var hold in holds) hold.SetResult([]);
        });
        Test("真实匹配调用将close令牌一路传到Core会话/FnNas/假HTTP；取消不写", async () =>
        {
            using var f = new FnNasFixture();
            var entered = new TaskCompletionSource<FnNasTransport.Request>(TaskCreationOptions.RunContinuationsAsynchronously);
            var hold = new TaskCompletionSource<HttpResponseMessage?>();
            f.Http.Before = request => { if (request.Path == "/v/api/v1/scrap/search") { entered.TrySetResult(request); return hold.Task; } return Task.FromResult<HttpResponseMessage?>(null); };
            FnNasMatchDialog? shown = null; Task? search = null;
            ContentDialog.ShowEndpoint = async control =>
            {
                shown = (FnNasMatchDialog)control; shown.KeywordBox.Text = "  输入  "; shown.SourceBox.SelectedIndex = 2;
                search = shown.SubmitFixture(); var request = await entered.Task;
                Check.True(request.Token.CanBeCanceled); Check.False(request.Token.IsCancellationRequested);
                using var body = JsonDocument.Parse(request.Body);
                Check.Equal("item", body.RootElement.GetProperty("item_guid").GetString()); Check.Equal("tmdb_id", body.RootElement.GetProperty("source").GetString());
                Check.Equal("输入", body.RootElement.GetProperty("source_keyword").GetString()); Check.Equal("zh-CN", body.RootElement.GetProperty("lan").GetString());
                return ContentDialogResult.None;
            };
            await f.MatchAsync(); await search!;
            Check.True((await entered.Task).Token.IsCancellationRequested); Check.Equal(1, f.Http.Searches); Check.Equal(0, f.Http.Writes);
            Check.Null(shown!.Results.ItemsSource); Check.Equal(0, f.Actions.Notices.Count);
            hold.SetResult(FnNasTransport.Reply("[]")); await Task.Delay(5); Check.Equal(0, f.Http.Writes);
        });
        Test("confirm后的Rescrap独立写所有权：关闭搜索不能取消/重放已授权写", async () =>
        {
            using var f = new FnNasFixture(); var changed = 0;
            var entered = new TaskCompletionSource<FnNasTransport.Request>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<HttpResponseMessage?>(); FnNasMatchDialog? shown = null;
            f.Http.Before = request => { if (request.Path == "/v/api/v1/scrap/rescrap") { entered.TrySetResult(request); return release.Task; } return Task.FromResult<HttpResponseMessage?>(null); };
            ContentDialog.ShowEndpoint = async control =>
            {
                shown = (FnNasMatchDialog)control; await shown.SubmitFixture();
                shown.Results.Select(((IReadOnlyList<FnNasScrapCandidate>)shown.Results.ItemsSource!)[0]);
                Check.False(shown.MatchFixture().Cancel); return ContentDialogResult.Primary;
            };
            var command = f.MatchAsync(() => changed++); var write = await entered.Task;
            Check.True(shown!.Selected is not null); Check.False(write.Token.IsCancellationRequested);
            shown.CloseFixture(); Check.False(write.Token.IsCancellationRequested);
            Check.Equal(1, f.Http.Writes); Check.Equal(0, changed);
            using var body = JsonDocument.Parse(write.Body); Check.Equal("42", body.RootElement.GetProperty("source_id").GetString());
            release.SetResult(null); await command;
            Check.Equal(1, f.Http.Writes); Check.Equal(1, changed); Check.Equal(1, f.Actions.Notices.Count);
            Check.Contains("已提交", f.Actions.Notices[0].Text);
        });
        Test("匿名旧搜索取消不撤销会话；重新打开可正常查询而不泄漏旧令牌", async () =>
        {
            using var f = new FnNasFixture(); FnNasMatchDialog? first = null;
            var hold = new TaskCompletionSource<HttpResponseMessage?>(); CancellationToken oldToken = default;
            f.Http.Before = request =>
            {
                if (request.Path == "/v/api/v1/scrap/search" && f.Http.Searches == 1) { oldToken = request.Token; return hold.Task; }
                return Task.FromResult<HttpResponseMessage?>(null);
            };
            ContentDialog.ShowEndpoint = control => { first = (FnNasMatchDialog)control; _ = first.SubmitFixture(); return Task.FromResult(ContentDialogResult.None); };
            await f.MatchAsync(); Check.True(oldToken.IsCancellationRequested); Check.True(f.Scope.IsAvailable);
            ContentDialog.ShowEndpoint = async control =>
            {
                var dialog = (FnNasMatchDialog)control; await dialog.SubmitFixture();
                Check.Equal(1, ((IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!).Count);
                Check.False(dialog.Problem.IsOpen); return ContentDialogResult.None;
            };
            await f.MatchAsync(); hold.SetException(new HttpRequestException("匿名旧错误")); await Task.Delay(5);
            Check.Equal(2, f.Http.Searches); Check.Equal(0, f.Http.Writes); Check.Equal(0, f.Actions.Notices.Count); Check.Null(first!.Results.ItemsSource);
        });
    }

    private static async Task QueuedCompletion(bool error)
    {
        var queue = new QueuedContext();
        var old = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fresh = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>(); var calls = 0;
        var dialog = new FnNasMatchDialog("匿名", (_, _, _) => ++calls == 1 ? old.Task : fresh.Task);
        dialog.OpenFixture(); var abandoned = Under(queue, dialog.SubmitFixture);
        if (error) old.SetException(new HttpRequestException("匿名排队旧错误")); else old.SetResult([Candidate]);
        await Check.Until(() => queue.Pending.Count > 0);
        dialog.CloseFixture(); dialog.OpenFixture(); var current = dialog.SubmitFixture();
        queue.Drain(); await abandoned;
        Check.Null(dialog.Results.ItemsSource); Check.False(dialog.Problem.IsOpen);
        Check.False(dialog.SearchButton.IsEnabled); Check.Equal(Visibility.Visible, dialog.Busy.Visibility);
        fresh.SetResult([Candidate with { Title = "新候选" }]); await current; queue.Drain();
        var rows = (IReadOnlyList<FnNasScrapCandidate>)dialog.Results.ItemsSource!;
        Check.Equal("新候选", rows[0].Title); Check.True(dialog.SearchButton.IsEnabled); Check.Equal(Visibility.Collapsed, dialog.Busy.Visibility);
        dialog.CloseFixture();
    }
    private static Task Under(QueuedContext queue, Func<Task> run)
    {
        var previous = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(queue);
        try { return run(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CreateClosedAsync(List<TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>> holds)
    {
        var hold = new TaskCompletionSource<IReadOnlyList<FnNasScrapResult>>(); holds.Add(hold);
        var dialog = new FnNasMatchDialog("匿名", (_, _, _) => hold.Task); dialog.OpenFixture(); var search = dialog.SubmitFixture();
        var reference = new WeakReference(dialog); dialog.CloseFixture(); await search; return reference;
    }
}
