using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CoreSourceContractTests
{
    internal static void Register()
    {
        Program.AsyncCase("R042 实际聚合 SelectSource 保留旧 scope，普通捕获只读任务仍允许完成", async _ =>
        {
            using var f = new SourceFixture();
            var notifications = 0;
            f.Session.SourcesChanged += (_, _) => notifications++;
            f.SwitchToOther();
            var read = await f.Scope.ExecuteAsync((client, token) => client.GetItemAsync("series-1", token), CancellationToken.None);
            Assert.Equal("series-1", read.Id);
            Assert.Equal(1, notifications);
            Assert.True(ReferenceEquals(read.Source, f.Scope));
            Assert.Equal("emby-fixture.invalid", f.Transport.Requests.Single().Host);
            f.AssertWrites(0, 0);
        });
        foreach (var movie in new[] { false, true })
        {
            Program.AsyncCase("R042 有效 " + (movie ? "电影" : "整剧") + " identify 保留实际确认目标和一次 Apply/Refresh", async _ =>
            {
                using var f = new SourceFixture(movie: movie);
                var results = await f.ExecuteConfirmedAsync(() => f.Scope.IsCurrent);
                Assert.True(results.Single().Success);
                f.AssertWrites(1, 1);
                var target = movie ? "movie-1" : "series-1";
                Assert.Contains("RemoteSearch/Apply/" + target, f.Replies.Requests.Single(request => request.Path.Contains("RemoteSearch/Apply", StringComparison.Ordinal)).Path);
                Assert.Equal(2, Searches(f), "已确认候选提交不重新搜索；中文/原文标题为两次实际只读请求");
            });
            Program.AsyncCase("R042 " + (movie ? "电影" : "整剧") + " RemoteSearch 等待中聚合切来源，旧 scope available 仍拒绝 Apply", async _ =>
            {
                using var f = new SourceFixture(movie: movie);
                await f.LoadPlanAsync();
                using var pause = f.Transport.PauseNext(IsSearch);
                var operation = CaptureAsync(() => f.ExecuteConfirmedAsync(() => f.Scope.IsCurrent));
                await pause.Started.Task;
                f.SwitchToOther();
                pause.Resume();
                var error = await operation;
                f.AssertWrites(0, 0);
                Assert.True(error is OperationCanceledException, "当前读取结束后拒绝生成/消费旧确认凭证");
                Assert.True(Searches(f) >= 1, "已发出的只读请求不能倒算成零；不为只读尾项扩产品补丁");
                Assert.False(pause.CancellationObserved, "聚合切换不会撤销旧 scope lifetime，是 canContinue 阻止写入");
            });
        }
        Program.AsyncCase("R042 identify-only 初始 canContinue=false，RemoteSearch/Apply/Refresh 全零", async _ =>
        {
            using var f = new SourceFixture();
            await f.LoadPlanAsync();
            var before = f.Replies.Requests.Count;
            var error = await CaptureAsync(() => f.ExecuteConfirmedAsync(() => false));
            Assert.True(error is OperationCanceledException);
            Assert.Equal(before, f.Replies.Requests.Count);
            Assert.Equal(0, Searches(f));
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 任意 canContinue 在 RemoteSearch 回话前失效，identify-only 不写入", async _ =>
        {
            using var f = new SourceFixture();
            await f.LoadPlanAsync();
            var current = true;
            using var pause = f.Transport.PauseNext(IsSearch);
            var operation = CaptureAsync(() => f.ExecuteConfirmedAsync(() => current));
            await pause.Started.Task;
            current = false;
            pause.Resume();
            var error = await operation;
            f.AssertWrites(0, 0);
            Assert.True(error is OperationCanceledException);
            Assert.True(f.Scope.IsCurrent, "这项单独验证委托契约，不冒充真实来源切换");
        });
        Program.AsyncCase("R042 identify-only Apply 已发后聚合切来源，不能再发 Refresh 或重放 Apply", async _ =>
        {
            using var f = new SourceFixture();
            using var pause = f.Transport.PauseNext(IsApply);
            var operation = f.ExecuteConfirmedAsync(() => f.Scope.IsCurrent);
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            var results = await operation;
            f.AssertWrites(1, 0);
            Assert.False(results.Single().Success, "保留已有识别结果未确认语义，不在 R042 修 R024 回读反馈");
            Assert.Contains("取消", results.Single().Message);
        });
        Program.AsyncCase("R042 两项 transfer 全成功后聚合切来源，保已成功回执且 identify/scan 全零", async _ =>
        {
            using var f = new SourceFixture(2, mixed: true);
            using var pause = f.Transport.PauseNext(request => IsTransferWrite(request) && f.Replies.TransferWrites == 2);
            var operation = f.ExecuteConfirmedAsync(() => f.Scope.IsCurrent);
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            var results = await operation;
            f.AssertWrites(0, 0, transfer: 2);
            Assert.Equal(3, results.Count);
            Assert.True(results[0].Success);
            Assert.True(results[1].Success);
            Assert.False(results[2].Success);
            Assert.Contains("已完成整理", results[0].Message);
            Assert.Contains("手动扫描", results[1].Message);
            Assert.Contains("取消", results[2].Message);
        });
        Program.AsyncCase("R042 transfer 回话前普通 canContinue=false，末尾 identify 不执行", async _ =>
        {
            using var f = new SourceFixture(1, mixed: true);
            var current = true;
            using var pause = f.Transport.PauseNext(IsTransferWrite);
            var operation = f.ExecuteConfirmedAsync(() => current);
            await pause.Started.Task;
            current = false;
            pause.Resume();
            var results = await operation;
            f.AssertWrites(0, 0, transfer: 1);
            Assert.True(results[0].Success);
            Assert.False(results[1].Success);
        });
        Program.AsyncCase("R042 首 transfer 完成后切来源，后续 transfer 和 identify 不提交", async _ =>
        {
            using var f = new SourceFixture(2, mixed: true);
            using var pause = f.Transport.PauseNext(IsTransferWrite);
            var operation = f.ExecuteConfirmedAsync(() => f.Scope.IsCurrent);
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            var results = await operation;
            f.AssertWrites(0, 0, transfer: 1);
            Assert.True(results[0].Success);
            Assert.False(results[1].Success);
            Assert.False(results[2].Success);
        });
        Program.AsyncCase("R042 非聚合切换实际 PruneOthers 撤销旧 lifetime，等待读取取消且零写入", async _ =>
        {
            using var f = new SourceFixture(aggregate: false);
            await f.LoadPlanAsync();
            using var pause = f.Transport.PauseNext(IsSearch);
            var operation = CaptureAsync(() => f.ExecuteConfirmedAsync(() => f.Scope.IsCurrent));
            await pause.Started.Task;
            f.SwitchToOther();
            var error = await operation;
            Assert.True(error is OperationCanceledException);
            Assert.True(pause.CancellationObserved);
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 显式取消当前 RemoteSearch 不伪造成成功，已发读保留且零写入", async _ =>
        {
            using var f = new SourceFixture();
            await f.LoadPlanAsync();
            using var cancel = new CancellationTokenSource();
            using var pause = f.Transport.PauseNext(IsSearch);
            var operation = CaptureAsync(() => f.ExecuteConfirmedAsync(() => true, cancel.Token));
            await pause.Started.Task;
            cancel.Cancel();
            Assert.True(await operation is OperationCanceledException);
            Assert.True(pause.CancellationObserved);
            Assert.Equal(1, Searches(f));
            f.AssertWrites(0, 0);
        });
#if !R042_LEGACY
        Program.AsyncCase("R042 已展示 identify 凭证在 Submit 前 canContinue=false，零新增请求且未 Claim 原批次", async _ =>
        {
            using var f = new SourceFixture();
            var prepared = (AniBtPreparedScrapePlan)await f.PrepareConfirmedAsync(() => true);
            var before = f.Replies.Requests.Count;
            Assert.True(await CaptureAsync(() => f.SubmitConfirmedAsync(prepared, () => false)) is OperationCanceledException);
            Assert.Equal(before, f.Replies.Requests.Count, "当前 Submit 不能重新搜索或发出写入");
            Assert.False(prepared.CanSubmit);
            f.Plan!.EnsureNotSubmitted();
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 mixed 提交整批历史重核等待中切来源，Claim/任何 write 前拒绝", async _ =>
        {
            using var f = new SourceFixture(1, mixed: true);
            var prepared = (AniBtPreparedScrapePlan)await f.PrepareConfirmedAsync(() => true);
            using var pause = f.Transport.PauseNext(request => request.Path.Contains("history/transfer", StringComparison.Ordinal));
            var operation = CaptureAsync(() => f.SubmitConfirmedAsync(prepared, () => f.Scope.IsCurrent));
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            Assert.True(await operation is OperationCanceledException);
            Assert.False(prepared.CanSubmit);
            f.Plan!.EnsureNotSubmitted();
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 已封存来源 A 的凭证不能交给来源 B 的实际 client", async _ =>
        {
            using var f = new SourceFixture();
            var prepared = (AniBtPreparedScrapePlan)await f.PrepareConfirmedAsync(() => true);
            var error = await CaptureAsync(() => f.SubmitConfirmedAsync(prepared, () => true, clientOverride: f.OtherScope.Client));
            Assert.True(error is MoviePilotOperationBlockedException);
            Assert.False(prepared.CanSubmit);
            f.Plan!.EnsureNotSubmitted();
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 已封存来源 A 的实际相同身份 client 可跨默认切换继续，除非 caller 拒绝", async _ =>
        {
            using var f = new SourceFixture();
            var prepared = await f.PrepareConfirmedAsync();
            f.SwitchToOther();
            var results = await f.SubmitConfirmedAsync(prepared);
            Assert.True(results.Single().Success, "scope 本来允许后台固定来源任务；不能全局改成 IsCurrent");
            f.AssertWrites(1, 1);
        });
        Program.AsyncCase("R042 当前一次计划已 Apply 后取消 Refresh，不得借相同凭证重发", async _ =>
        {
            using var f = new SourceFixture();
            var prepared = await f.PrepareConfirmedAsync();
            var current = true;
            using var pause = f.Transport.PauseNext(IsApply);
            var operation = f.SubmitConfirmedAsync(prepared, () => current);
            await pause.Started.Task;
            current = false;
            pause.Resume();
            Assert.False((await operation).Single().Success);
            f.AssertWrites(1, 0);
            Assert.True(await CaptureAsync(() => f.SubmitConfirmedAsync(prepared, () => true)) is MoviePilotOperationBlockedException);
            f.AssertWrites(1, 0);
        });
#endif
    }

    internal static int Searches(SourceFixture f) => f.Replies.Requests.Count(request => request.Path.Contains("Items/RemoteSearch/Series", StringComparison.Ordinal)
        || request.Path.Contains("Items/RemoteSearch/Movie", StringComparison.Ordinal));
    internal static bool IsSearch(SourceRequest request) => request.Path.Contains("Items/RemoteSearch/Series", StringComparison.Ordinal)
        || request.Path.Contains("Items/RemoteSearch/Movie", StringComparison.Ordinal);
    internal static bool IsApply(SourceRequest request) => request.Path.Contains("RemoteSearch/Apply", StringComparison.Ordinal);
    internal static bool IsTransferWrite(SourceRequest request) => request.Write && request.Path.Contains("transfer/manual?", StringComparison.Ordinal);

    internal static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try { await operation(); return null; }
        catch (Exception error) { return error; }
    }
}
