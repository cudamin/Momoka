using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        CandidateCases();
        CoreCases();
        DialogCases();
        return Run();
    }

    private static void AsyncCase(string name, Func<UiQueue, Task> run) => Test(name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(() => run(queue));
    });

    private static void CandidateCases()
    {
        Test("R037 [无关,精确]选精确，保全角规范化", () =>
        {
            var wrong = new RemoteSearchResult { Name = "另一部动画", ProductionYear = 2026 };
            var exact = new RemoteSearchResult { Name = "匿名动画！", ProductionYear = 2026 };
            Assert.True(ReferenceEquals(exact, AniBtService.PickIdentifyResult([wrong, exact], ["匿名动画!"], 2026)));
        });
        Test("R037 只有无关结果不回首条", () => Assert.Null(AniBtService.PickIdentifyResult(
            [new RemoteSearchResult { Name = "无关电影", ProductionYear = 2026 }], ["匿名动画"], 2026)));
        Test("R037 所有年份不符不回首条", () => Assert.Null(AniBtService.PickIdentifyResult(
            [new RemoteSearchResult { Name = "匿名动画", ProductionYear = 2014 }, new RemoteSearchResult { Name = "匿名动画", ProductionYear = 2018 }], ["匿名动画"], 2026)));
        Test("R037 不以标题包含关系认同一作品", () => Assert.Null(AniBtService.PickIdentifyResult(
            [new RemoteSearchResult { Name = "匿名动画 续作", ProductionYear = 2026 }], ["匿名动画"], 2026)));
        Test("R037 正常跨语言精确候选与既有年份一岁容差", () =>
        {
            var japanese = new RemoteSearchResult { Name = "Anonymous Anime", ProductionYear = 2025 };
            Assert.True(ReferenceEquals(japanese, AniBtService.PickIdentifyResult([japanese], ["匿名动画", "Anonymous Anime"], 2026)));
        });
        Test("R037 年份相符优先于一岁容差或缺年份", () =>
        {
            var missing = new RemoteSearchResult { Name = "匿名动画" };
            var near = new RemoteSearchResult { Name = "匿名动画", ProductionYear = 2025 };
            var exact = new RemoteSearchResult { Name = "匿名动画", ProductionYear = 2026 };
            Assert.True(ReferenceEquals(exact, AniBtService.PickIdentifyResult([missing, near, exact], ["匿名动画"], 2026)));
        });
    }

    private static void CoreCases()
    {
        AsyncCase("R037 实际 Core 准备只读，年份传真实 RemoteSearch", async _ =>
        {
            using var f = new AnonymousFixture();
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            Assert.True(prepared is not null);
            foreach (var search in f.Handler.Requests.Where(request => request.Path.Contains("RemoteSearch/Series", StringComparison.Ordinal)))
            {
                using var body = JsonDocument.Parse(search.Body);
                Assert.Equal(2026, body.RootElement.GetProperty("SearchInfo").GetProperty("Year").GetInt32());
                Assert.Equal("series-1", body.RootElement.GetProperty("ItemId").GetString());
                Assert.Equal(0, body.RootElement.GetProperty("SearchInfo").GetProperty("ProviderIds").EnumerateObject().Count());
            }
        });
        foreach (var (name, reply) in new[]
        {
            ("只有无关", "[{\"Name\":\"无关电影\",\"ProductionYear\":2026}]"),
            ("全部年份不符", "[{\"Name\":\"匿名动画\",\"ProductionYear\":2010},{\"Name\":\"Anonymous Anime\",\"ProductionYear\":2012}]"),
            ("只匹配包含", "[{\"Name\":\"匿名动画 第二部\",\"ProductionYear\":2026}]"),
            ("空结果", "[]")
        }) AsyncCase("R037 实际 Core " + name + "拒绝且0Apply0Refresh", async _ =>
        {
            using var f = new AnonymousFixture();
            f.Handler.SearchReply = reply;
            var error = await CaptureAsync(() => f.PrepareActualAsync());
            f.AssertZeroWrites();
            Assert.True(error is MoviePilotOperationBlockedException);
            Assert.Contains("普通识别", error!.Message);
        });
        AsyncCase("R037 实际 Core 确认才Apply一次并保持已确认候选", async _ =>
        {
            using var f = new AnonymousFixture();
            f.Handler.SearchReply = "[{\"Name\":\"无关电影\",\"ProductionYear\":2026},{\"Name\":\"Anonymous Anime\",\"ProductionYear\":2026,\"ProviderIds\":{\"Tmdb\":\"exact-fixture\"},\"SearchProviderName\":\"TheMovieDb\",\"FutureField\":\"keep\"}]";
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            Assert.True(prepared is not null);
            var searches = f.Handler.Requests.Count(request => request.Path.Contains("RemoteSearch/Series", StringComparison.Ordinal));
            f.Handler.SearchReply = "[{\"Name\":\"另外的目标\",\"ProductionYear\":2026}]";
            var results = await f.SubmitActualAsync(prepared!);
            Assert.True(results.Single().Success);
            Assert.Equal(1, f.Handler.ApplyCount);
            Assert.Equal(1, f.Handler.RefreshCount);
            Assert.Equal(searches, f.Handler.Requests.Count(request => request.Path.Contains("RemoteSearch/Series", StringComparison.Ordinal)));
            var apply = f.Handler.Requests.Single(request => request.Path.Contains("RemoteSearch/Apply", StringComparison.Ordinal));
            using var body = JsonDocument.Parse(apply.Body);
            Assert.Equal("Anonymous Anime", body.RootElement.GetProperty("Name").GetString());
            Assert.Equal("exact-fixture", body.RootElement.GetProperty("ProviderIds").GetProperty("Tmdb").GetString());
            Assert.Equal("keep", body.RootElement.GetProperty("FutureField").GetString());
            Assert.Contains("ReplaceAllImages=true", apply.Path);
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!)) is MoviePilotOperationBlockedException);
            Assert.Equal(1, f.Handler.ApplyCount);
        });
        AsyncCase("R039 实际 Core history准备0write，确认使用同一sealed preview", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            Assert.Equal(1, f.Handler.Previews);
            Assert.True(prepared is not null);
            f.Handler.PreviewSuffix = "server-later";
            var result = await f.SubmitActualAsync(prepared!);
            Assert.Equal(1, f.Handler.TransferWrites);
            Assert.Equal(1, f.Handler.Previews, "正式提交不可重新预览替换已确认目标");
            var previews = f.Handler.Requests.Where(request => request.Path.Contains("transfer/manual?", StringComparison.Ordinal)).ToArray();
            using var preview = JsonDocument.Parse(previews[0].Body);
            using var write = JsonDocument.Parse(previews[1].Body);
            foreach (var property in preview.RootElement.EnumerateObject().Where(property => property.Name != "preview"))
                Assert.True(JsonElement.DeepEquals(property.Value, write.RootElement.GetProperty(property.Name)), "实际write必须沿用preview的请求：" + property.Name);
            Assert.True(result.Single().Success);
        });
        AsyncCase("R039 实际 Core failed预览整批0write", async _ =>
        {
            using var f = new AnonymousFixture(2);
            f.Handler.BadPreview = true;
            var error = await CaptureAsync(() => f.PrepareActualAsync());
            f.AssertZeroWrites();
            Assert.True(error is MoviePilotOperationBlockedException);
        });
        AsyncCase("R039 实际 Core mixed所有读取预览先于第一个write", async _ =>
        {
            using var f = new AnonymousFixture(2, mixed: true);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            var lastRead = f.Handler.Requests.Count;
            await f.SubmitActualAsync(prepared!);
            var firstWrite = f.Handler.Requests.ToList().FindIndex(request => request.Write);
            Assert.True(firstWrite >= lastRead);
            Assert.Equal(2, f.Handler.Previews);
            Assert.Equal(2, f.Handler.TransferWrites);
            Assert.Equal(1, f.Handler.ApplyCount);
            Assert.Equal(1, f.Handler.RefreshCount);
            Assert.False(f.Handler.Requests.Skip(firstWrite).Any(request => request.Path.Contains("RemoteSearch/Series", StringComparison.Ordinal) || !request.Write && request.Path.Contains("transfer/manual?", StringComparison.Ordinal)));
        });
        AsyncCase("R039 mixed末尾识别失败阻止先前有效预览write", async _ =>
        {
            using var f = new AnonymousFixture(1, mixed: true);
            f.Handler.SearchReply = "[{\"Name\":\"无关电影\",\"ProductionYear\":2026}]";
            var error = await CaptureAsync(() => f.PrepareActualAsync());
            f.AssertZeroWrites();
            Assert.Equal(1, f.Handler.Previews);
            Assert.True(error is MoviePilotOperationBlockedException);
        });
        AsyncCase("R039 history确认前原记录改变拒绝0write", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Handler.ChangedHistory = true;
            var error = await CaptureAsync(() => f.SubmitActualAsync(prepared!));
            Assert.True(error is MoviePilotTransferBlockedException);
            f.AssertZeroWrites();
            Assert.Contains("改变", error!.Message);
        });
        AsyncCase("R039 准备后MP连接改变0write", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Settings.MoviePilot.Url = "https://changed-mp.invalid/";
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!)) is MoviePilotException);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 准备后scope守卫改变0write", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!, canContinue: () => false)) is OperationCanceledException);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 准备后预先取消0write", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!, cancel.Token)) is OperationCanceledException);
            f.AssertZeroWrites();
        });
        AsyncCase("R037 准备后Emby连接改变拒绝0write", async _ =>
        {
            using var f = new AnonymousFixture();
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            var replacement = new EmbyClient(new EmbyHttp(f.Handler), f.Emby.Connection with { UserId = "different-account" });
            var submit = typeof(AniBtService).GetMethods().Single(method => method.Name == "SubmitAsync" && method.GetParameters()[1].ParameterType == prepared!.GetType());
            var task = (Task)submit.Invoke(f.Service, [replacement, prepared, CancellationToken.None, null])!;
            Assert.True(await CaptureAsync(async () => await task) is MoviePilotOperationBlockedException);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 取消已准备计划后旧对象不能submit或重新claim", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            prepared!.GetType().GetMethod("Invalidate")!.Invoke(prepared, null);
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared)) is MoviePilotOperationBlockedException);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 unknown丢回执停止后项，同计划和新预览均不重发", async _ =>
        {
            using var f = new AnonymousFixture(2);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Handler.LostReceipt = true;
            var results = await f.SubmitActualAsync(prepared!);
            Assert.False(results[0].Success);
            Assert.Contains("勿重复提交", results[0].Message);
            Assert.Contains("没有提交", results[1].Message);
            Assert.Equal(1, f.Handler.TransferWrites);
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!)) is MoviePilotOperationBlockedException);
            f.Handler.LostReceipt = false;
            f.InjectCachedAvailability();
            await f.PrepareInitialAsync();
            var again = await f.PrepareActualAsync();
            var retry = await f.SubmitActualAsync(again!);
            Assert.False(retry[0].Success);
            Assert.Equal(1, f.Handler.TransferWrites);
        });
        AsyncCase("R038保留 completed扫描失败不改整理成功或解除防重", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Handler.ScanFailure = true;
            var result = (await f.SubmitActualAsync(prepared!)).Single();
            Assert.True(result.Success);
            Assert.Contains("扫描请求失败", result.Message);
            Assert.Contains("勿重复整理", result.Message);
            Assert.Equal(1, f.Handler.TransferWrites);
        });
        AsyncCase("R039 failed部分成功回执继续真实逐项反馈但不允许重整重发", async _ =>
        {
            using var f = new AnonymousFixture(2);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Handler.FailedReceipt = true;
            var result = await f.SubmitActualAsync(prepared!);
            Assert.False(result[0].Success);
            Assert.Contains("整理失败", result[0].Message);
            Assert.Equal(2, f.Handler.TransferWrites, "保留现有已处理失败回执不误判传输异常的契约");
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!)) is MoviePilotOperationBlockedException);
            Assert.Equal(2, f.Handler.TransferWrites);
        });
        AsyncCase("R039 mixed确认核验后项history改变阻止全部write", async _ =>
        {
            using var f = new AnonymousFixture(2, mixed: true);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Handler.ChangedHistoryIndex = 1;
            var error = await CaptureAsync(() => f.SubmitActualAsync(prepared!));
            Assert.True(error is MoviePilotTransferBlockedException);
            f.AssertZeroWrites();
            f.Handler.ChangedHistoryIndex = -1;
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!)) is MoviePilotOperationBlockedException);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 mixed确认核验读取失败阻止全部write", async _ =>
        {
            using var f = new AnonymousFixture(1, mixed: true);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Handler.HistoryReadFailure = true;
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!)) is MoviePilotException);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 已准备凭证不接受另一Service扩权提交", async _ =>
        {
            using var f = new AnonymousFixture(1);
            using var other = new AnonymousFixture(1);
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            var error = await CaptureAsync(() => other.SubmitActualAsync(prepared!));
            Assert.True(error is MoviePilotOperationBlockedException);
            f.AssertZeroWrites();
            other.AssertZeroWrites();
        });
        AsyncCase("R039 只读计划不能通过伪造title或year扩大候选写权", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var fake = AnonymousFixture.Anime with { Title = "伪造作品", Year = 2014 };
            Assert.True(await CaptureAsync(() => f.PrepareActualAsync([fake])) is MoviePilotOperationBlockedException);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 两份同批preview只有一份可消费", async _ =>
        {
            using var f = new AnonymousFixture(1);
            var first = await f.PrepareActualAsync();
            var second = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            await f.SubmitActualAsync(first!);
            var writes = f.Handler.Writes;
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(second!)) is MoviePilotOperationBlockedException);
            Assert.Equal(writes, f.Handler.Writes);
        });
        AsyncCase("R024欠项保持 Apply成功Refresh失败不重发Apply", async _ =>
        {
            using var f = new AnonymousFixture();
            var prepared = await f.PrepareActualAsync();
            f.AssertZeroWrites();
            f.Handler.RefreshFailure = true;
            var rows = await f.SubmitActualAsync(prepared!);
            Assert.False(rows.Single().Success);
            Assert.Equal(1, f.Handler.ApplyCount);
            Assert.Equal(1, f.Handler.RefreshCount);
            Assert.True(await CaptureAsync(() => f.SubmitActualAsync(prepared!)) is MoviePilotOperationBlockedException);
            Assert.Equal(1, f.Handler.ApplyCount);
        });
    }

    private static void DialogCases()
    {
        AsyncCase("R037 实际Dialog第一击显示最终整剧候选和覆盖影响，第二击才write", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.Equal("确认执行", dialog.PrimaryButtonText);
            var row = dialog.ViewModel.Rows.Single();
            var text = PreviewText(dialog, row);
            Assert.Contains("实际库内整剧", text);
            Assert.Contains("series-1", text);
            Assert.Contains("整部剧", text);
            Assert.Contains("匿名动画", text);
            Assert.Contains("2026", text);
            Assert.Contains("元数据", text);
            Assert.Contains("图片", text);
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.ApplyCount);
            Assert.Equal(1, f.Handler.RefreshCount);
            Assert.False(dialog.IsPrimaryButtonEnabled);
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.ApplyCount);
        });
        AsyncCase("R037 实际Dialog电影计划显示真实电影目标而不是整剧", async queue =>
        {
            using var f = new AnonymousFixture(movie: true);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            var text = PreviewText(dialog, dialog.ViewModel.Rows.Single());
            Assert.Contains("实际库内电影", text);
            Assert.Contains("movie-1", text);
            Assert.Contains("电影", text);
        });
        AsyncCase("R039 实际Dialog history真实Source Target方式旧目标均可核验", async queue =>
        {
            using var f = new AnonymousFixture(1);
            f.Handler.TransferType = "softlink";
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            var text = PreviewText(dialog, dialog.ViewModel.Rows.Single());
            Assert.Contains(AnonymousHandler.OldPath(0), text);
            Assert.Contains(AnonymousHandler.NewPath(0), text);
            Assert.Contains("softlink", text);
            Assert.Contains("旧目标", text);
            Assert.Contains("旧历史", text);
            Assert.Contains("清理", text);
            Assert.Contains("#1", text);
            Assert.Equal(1, f.Handler.Previews);
        });
        AsyncCase("R039 实际Dialog取消预览使绑定收起且0preview:false", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.True(dialog.HasControl("CancelPreview"));
            dialog.ClickForFixture("CancelPreview");
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("PreparedPlan", "Visibility", dialog.ViewModel.Rows.Single()));
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("CancelPreview", "Visibility"));
            f.AssertZeroWrites();
        });
        AsyncCase("R039 实际Dialog关闭取消整个VM，不能再submit", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.True(dialog.CloseForFixture());
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.False(dialog.IsPrimaryButtonEnabled);
        });
        AsyncCase("R039 实际VM更改AniBT选择使原preview失效，下一击仅重新预览", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            var row = dialog.ViewModel.Rows.Single();
            row.Selected = row.Candidates.Single(choice => choice.BgmId == 43);
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("PreparedPlan", "Visibility", row));
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.Equal(2, f.Handler.Previews);
            Assert.Equal("确认执行", dialog.PrimaryButtonText);
        });
        AsyncCase("R037 实际VM读取失败不显示确认按钮且0write", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            f.Handler.ReadFailure = true;
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.True(dialog.ViewModel.NoticeOpen);
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("PreparedPlan", "Visibility", dialog.ViewModel.Rows.Single()));
        });
        AsyncCase("R039 实际Dialog scope变更后确认0write", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            f.SetCurrent(f.OtherScope);
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
        });
        AsyncCase("R037 实际Dialog Emby连接变更后确认0write", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            f.ChangeEmbyConnection();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 实际Dialog MP连接变更后确认0write", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            f.Settings.MoviePilot.Enabled = false;
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.True(dialog.ViewModel.NoticeOpen);
        });
        AsyncCase("R037 实际VM预览等待中变更选择拒绝迟到preview", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            f.Handler.PauseNextSearch();
            var click = dialog.PrimaryForFixtureAsync(queue);
            await f.Handler.PauseStarted!.Task;
            var row = dialog.ViewModel.Rows.Single();
            row.Selected = row.Candidates.Single(choice => choice.BgmId == 43);
            f.Handler.ResumeSearch();
            await click;
            f.AssertZeroWrites();
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("PreparedPlan", "Visibility", row));
        });
        AsyncCase("R039 实际VM失败preview不可确认，恢复后须重新只读", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            f.Handler.BadPreview = true;
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            f.Handler.BadPreview = false;
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.Equal("确认执行", dialog.PrimaryButtonText);
            Assert.Equal(2, f.Handler.Previews);
        });
        AsyncCase("R039 实际Dialog unknown回执保锁，直接按钮和新预览均不再write", async queue =>
        {
            using var f = new AnonymousFixture(2);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            f.Handler.LostReceipt = true;
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.TransferWrites);
            Assert.Contains("勿重复提交", dialog.ViewModel.Rows[0].Result);
            Assert.Contains("没有提交", dialog.ViewModel.Rows[1].Result);
            Assert.False(dialog.IsPrimaryButtonEnabled);
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.TransferWrites);
        });
        AsyncCase("R039 实际XAML绑定动态更新生成成员，控件句柄主题角色", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            Assert.True(ReferenceEquals(dialog.ViewModel.Rows, dialog.BoundValue("ScrapeFiles", "ItemsSource")));
            Assert.True(dialog.HasControl("AnimeChoice"));
            Assert.True(dialog.HasControl("CancelPreview"));
            foreach (var element in dialog.Markup.Descendants().Where(element => element.Attributes().Any(attribute => attribute.Value.Contains("Preview", StringComparison.Ordinal))))
                foreach (var attribute in element.Attributes().Where(attribute => attribute.Value.StartsWith("{x:Bind", StringComparison.Ordinal)))
                    Assert.Contains("Mode=OneWay", attribute.Value);
            foreach (var text in dialog.Markup.Descendants().Where(element => element.Name.LocalName == "TextBlock"))
            {
                var name = text.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;
                if (name is not null)
                    Assert.False(text.Attribute("Text")?.Value.StartsWith("{x:Bind " + name + ",", StringComparison.Ordinal) == true,
                        "控件名不能遮蔽DataTemplate实际成员，通用反射边界不能代替XAML生成器");
                Assert.True(text.Attribute("Style")?.Value.StartsWith("{StaticResource Eg", StringComparison.Ordinal) == true);
                Assert.True(text.Attribute("FontSize") is null);
                Assert.True(text.Attribute("Foreground") is null || text.Attribute("Foreground")!.Value.StartsWith("{ThemeResource Eg", StringComparison.Ordinal));
            }
            Assert.True(typeof(AniBtScrapeRowViewModel).GetProperty("Selected")!.SetMethod is not null, "真实8.4.2生成器setter");
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.Equal(Visibility.Visible, dialog.BoundValue("CancelPreview", "Visibility"));
        });
        AsyncCase("R039 实际Dialog只读preview在途关闭立即撤销且0write", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            f.Handler.PauseNextSearch();
            var click = dialog.PrimaryForFixtureAsync(queue);
            await f.Handler.PauseStarted!.Task;
            Assert.True(dialog.CloseForFixture(), "只读阶段必须可关闭/取消；不能拿写入锁挡取消");
            f.Handler.ResumeSearch();
            await click;
            f.AssertZeroWrites();
            Assert.False(dialog.IsPrimaryButtonEnabled);
        });
        AsyncCase("R037 实际Dialog初始读取scope失效不认领迟到行", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            f.Handler.OnRequest = request =>
            {
                if (request.Path.Contains("torrent-match", StringComparison.Ordinal)) f.SetCurrent(f.OtherScope);
            };
            dialog.OpenForFixture();
            await queue.IdleAsync();
            Assert.Equal(0, dialog.ViewModel.Rows.Count);
            Assert.False(dialog.IsPrimaryButtonEnabled);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 实际Dialog预览期双击不会误转提交", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            f.Handler.PauseNextSearch();
            var first = dialog.PrimaryForFixtureAsync(queue);
            await f.Handler.PauseStarted!.Task;
            var second = dialog.PrimaryForFixtureAsync(queue);
            f.Handler.ResumeSearch();
            await Task.WhenAll(first, second);
            f.AssertZeroWrites();
            Assert.Equal("确认执行", dialog.PrimaryButtonText);
        });
        AsyncCase("R039 实际Dialog选择A到B到A仍清除旧preview", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            var row = dialog.ViewModel.Rows.Single();
            var original = row.Selected;
            row.Selected = row.Candidates.Single(choice => choice.BgmId == 43);
            row.Selected = original;
            Assert.Equal("预览计划", dialog.PrimaryButtonText);
            Assert.Equal(Visibility.Collapsed, dialog.BoundValue("PreparedPlan", "Visibility", row));
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(2, f.Handler.Previews);
            f.AssertZeroWrites();
        });
        AsyncCase("R039 实际Dialog确认后结果不明改选取消不能解锁", async queue =>
        {
            using var f = new AnonymousFixture(1);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            f.Handler.UnknownReceipt = true;
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.TransferWrites);
            dialog.ViewModel.Rows[0].Selected = dialog.ViewModel.Rows[0].Candidates[1];
            var cancel = typeof(AniBtScrapeViewModel).GetMethod("CancelPreview", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(cancel is not null, "实际VM必须提供预览取消边界");
            cancel!.Invoke(dialog.ViewModel, null);
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.TransferWrites);
            Assert.False(dialog.IsPrimaryButtonEnabled);
        });
        AsyncCase("R037 实际VM Apply后连接变化不刷新旧目标", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            f.Handler.OnRequest = request =>
            {
                if (request.Path.Contains("RemoteSearch/Apply", StringComparison.Ordinal)) f.ChangeEmbyConnection();
            };
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.ApplyCount);
            Assert.Equal(0, f.Handler.RefreshCount, "连接身份已改变后不得向旧目标发第二个write");
            Assert.False(dialog.IsPrimaryButtonEnabled);
        });
        AsyncCase("R039 实际VM首history写后连接变化停止后项和旧库扫描", async queue =>
        {
            using var f = new AnonymousFixture(2);
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            f.Handler.OnRequest = request =>
            {
                if (request.Write && request.Path.Contains("transfer/manual?", StringComparison.Ordinal)) f.ChangeEmbyConnection();
            };
            await dialog.PrimaryForFixtureAsync(queue);
            Assert.Equal(1, f.Handler.TransferWrites);
            Assert.Equal(0, f.Handler.Requests.Count(request => request.Path.Contains("Library/Refresh", StringComparison.Ordinal)));
            Assert.Contains("取消或连接改变", dialog.ViewModel.Rows[1].Result);
        });
        AsyncCase("R037 初始计划后Emby连接变化不得借新连接预览旧文件", async queue =>
        {
            using var f = new AnonymousFixture();
            var dialog = f.Dialog();
            dialog.OpenForFixture();
            await queue.IdleAsync();
            f.ChangeEmbyConnection();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertZeroWrites();
            Assert.Equal(0, f.Handler.Requests.Count(request => request.Path.Contains("RemoteSearch/Series", StringComparison.Ordinal)),
                "旧文件计划不能从新连接读取/确认另一个实际目标");
            Assert.False(dialog.IsPrimaryButtonEnabled);
        });
    }

    private static string PreviewText(AniBtScrapeDialog dialog, AniBtScrapeRowViewModel row)
    {
        Assert.Equal(Visibility.Visible, dialog.BoundValue("PreparedPlan", "Visibility", row));
        return dialog.BoundValue("PreviewHeadingText", "Text", row) + "\n" + dialog.BoundValue("PreviewDetailsText", "Text", row);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }
}
